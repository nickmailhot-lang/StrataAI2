using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.Runtime;

namespace StrataAI.Infrastructure.WorkManagement;

public static class AttachmentRuntimeRegistration
{
    public static bool AddAttachmentRuntime(this IServiceCollection services, IConfiguration configuration,
        RuntimeDescriptor runtime, bool worker)
    {
        var flag = configuration["STRATAAI_ATTACHMENT_STORAGE_ENABLED"];
        if (flag is null) return false;
        if (!bool.TryParse(flag, out var enabled)) throw new InvalidOperationException("Attachment storage enablement is invalid.");
        if (!enabled) return false;
        if (runtime.Mode != RuntimeMode.Production)
            throw new InvalidOperationException("Managed attachment storage requires Production mode.");
        var bucket = configuration["STRATAAI_ATTACHMENT_S3_BUCKET"] ?? "";
        var owner = configuration["STRATAAI_ATTACHMENT_S3_OWNER"] ?? "";
        S3AttachmentObjectStorage.ValidateBucketConfiguration(bucket, owner);
        var region = configuration["STRATAAI_ATTACHMENT_S3_REGION"];
        var endpoint = RegionEndpoint.EnumerableAllRegions.FirstOrDefault(value => value.SystemName == region);
        if (endpoint is null) throw new InvalidOperationException("A supported explicit attachment storage region is required.");
        ClamAvAttachmentMalwareScanner? scanner = null;
        if (worker)
        {
            if (string.IsNullOrWhiteSpace(configuration["STRATAAI_WORKER_ORGANIZATION_IDS"]))
                throw new InvalidOperationException("Attachment scanning requires explicit Worker Organization scope.");
            scanner = new ClamAvAttachmentMalwareScanner(configuration["STRATAAI_ATTACHMENT_SCANNER_SOCKET"] ?? "", TimeSpan.FromSeconds(30));
            services.AddSingleton(scanner);
            services.AddSingleton<IAttachmentMalwareScanner>(scanner);
            services.AddSingleton<AttachmentQuarantineScanner>();
            services.AddSingleton<IAttachmentScanDeliveryStore, PostgresAttachmentScanDeliveryStore>();
            services.AddSingleton<IBackgroundJobHandler, AttachmentScanDeliveryHandler>();
        }
        // Credentials are supplied by the official SDK credential chain (for
        // example workload IAM). Never accept HTTP/custom endpoints or a local
        // fallback in Production. DI owns and disposes the SDK client.
        services.TryAddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
        { RegionEndpoint = endpoint, UseHttp = false, MaxErrorRetry = 1 }));
        services.AddSingleton(provider => new S3AttachmentObjectStorage(provider.GetRequiredService<IAmazonS3>(), bucket, owner));
        services.AddSingleton<IAttachmentObjectStorage>(provider => provider.GetRequiredService<S3AttachmentObjectStorage>());
        services.Replace(ServiceDescriptor.Singleton<IRuntimeDependencyStatus>(provider =>
            new AttachmentRuntimeDependencyStatus(new ProductionRuntimeDependencyStatus(provider.GetRequiredService<PostgresConnectionFactory>()),
                provider.GetRequiredService<S3AttachmentObjectStorage>(), scanner)));
        return true;
    }
}

public sealed class AttachmentRuntimeDependencyStatus(IRuntimeDependencyStatus database,
    S3AttachmentObjectStorage storage, ClamAvAttachmentMalwareScanner? scanner = null) : IRuntimeDependencyStatus
{
    public RuntimeMode Mode => RuntimeMode.Production;
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            if (!await database.IsReadyAsync(deadline.Token).WaitAsync(deadline.Token)) return false;
            await storage.ValidatePrivateBucketAsync(deadline.Token).WaitAsync(deadline.Token);
            return scanner is null || await scanner.IsReadyAsync(deadline.Token).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { return false; }
    }
}
