using Amazon;
using Amazon.S3;
using System.Globalization;
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
    public static void InitializeAttachmentRuntime(this IServiceProvider provider, bool enabled)
    {
        if (!enabled) return;
        try { _ = provider.GetRequiredService<S3AttachmentObjectStorage>(); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Resolve the client at startup, before readiness routing;
            // constructor/provider errors must not turn /readyz into a 500 or
            // disclose credential-chain diagnostics in host startup logs.
            throw new InvalidOperationException("Attachment runtime initialization is unavailable.");
        }
    }

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
        var maximumText = configuration["STRATAAI_ATTACHMENT_MAX_BYTES"] ?? "20971520";
        if (!long.TryParse(maximumText, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum))
            throw new InvalidOperationException("Attachment upload size policy is invalid.");
        AttachmentUploadPolicy policy;
        try
        {
            policy = new AttachmentUploadPolicy(maximum,
                (configuration["STRATAAI_ATTACHMENT_ALLOWED_TYPES"] ?? "image/png,image/jpeg,image/webp,application/pdf")
                    .Split(',', StringSplitOptions.TrimEntries));
        }
        catch (ArgumentException) { throw new InvalidOperationException("Attachment upload policy is invalid."); }
        services.AddSingleton(policy);
        services.AddSingleton<IAttachmentFileTypeInspector, AttachmentFileTypeInspector>();
        ClamAvAttachmentMalwareScanner? scanner = null;
        if (worker)
        {
            services.AddSingleton<IAttachmentImagePreviewGenerator, LinuxIsolatedAttachmentImagePreviewGenerator>();
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
        if (!worker) services.AddSingleton<IAttachmentDownloadPreparer, PrivateAttachmentDownloadPreparer>();
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
