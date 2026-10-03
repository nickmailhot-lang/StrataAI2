using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using StrataAI.Infrastructure.Persistence;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class AttachmentRuntimeTests
{
    private static readonly RuntimeDescriptor Production = new(RuntimeMode.Production, "fixture", "fixture");
    private static Dictionary<string, string?> Settings() => new()
    {
        ["STRATAAI_ATTACHMENT_STORAGE_ENABLED"] = "true",
        ["STRATAAI_ATTACHMENT_S3_BUCKET"] = "strata-private-fixture",
        ["STRATAAI_ATTACHMENT_S3_OWNER"] = "123456789012",
        ["STRATAAI_ATTACHMENT_S3_REGION"] = "us-east-1",
        ["STRATAAI_ATTACHMENT_SCANNER_SOCKET"] = "/tmp/strata-scanner-fixture",
        ["STRATAAI_WORKER_ORGANIZATION_IDS"] = Guid.NewGuid().ToString()
    };
    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task ARCH_07_Worker_wiring_registers_managed_storage_scanner_and_exact_handler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAmazonS3>(new Client());
        services.AddSingleton(_ => new PostgresConnectionFactory("Host=localhost;Database=fixture;Username=fixture;Password=fixture"));
        Assert.True(services.AddAttachmentRuntime(Config(Settings()), Production, worker: true));
        await using var provider = services.BuildServiceProvider();
        Assert.IsType<S3AttachmentObjectStorage>(provider.GetRequiredService<IAttachmentObjectStorage>());
        Assert.IsType<ClamAvAttachmentMalwareScanner>(provider.GetRequiredService<IAttachmentMalwareScanner>());
        Assert.IsType<PostgresAttachmentScanDeliveryStore>(provider.GetRequiredService<IAttachmentScanDeliveryStore>());
        var handler = Assert.IsType<AttachmentScanDeliveryHandler>(Assert.Single(provider.GetServices<IBackgroundJobHandler>()));
        Assert.Equal(AttachmentScanJobs.Type, handler.JobType); Assert.Equal(AttachmentScanJobs.Service, handler.ServiceIdentity);
        Assert.NotNull(provider.GetRequiredService<AttachmentQuarantineScanner>());
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IAttachmentDownloadPreparer));
        Assert.Equal(20971520, provider.GetRequiredService<AttachmentUploadPolicy>().MaximumBytes);
        Assert.Equal(4, provider.GetRequiredService<AttachmentUploadPolicy>().AllowedMimeTypes.Count);
        Assert.IsType<AttachmentFileTypeInspector>(provider.GetRequiredService<IAttachmentFileTypeInspector>());
        provider.InitializeAttachmentRuntime(enabled: true);
    }

    [Fact]
    public void ARCH_07_API_does_not_register_worker_capabilities()
    {
        var settings = Settings(); settings.Remove("STRATAAI_ATTACHMENT_SCANNER_SOCKET"); settings.Remove("STRATAAI_WORKER_ORGANIZATION_IDS");
        var services = new ServiceCollection();
        Assert.True(services.AddAttachmentRuntime(Config(settings), Production, worker: false));
        Assert.Contains(services, s => s.ServiceType == typeof(IAttachmentDownloadPreparer) && s.ImplementationType == typeof(PrivateAttachmentDownloadPreparer));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IAttachmentMalwareScanner) || s.ServiceType == typeof(IAttachmentScanDeliveryStore));
    }

    [Theory]
    [InlineData("STRATAAI_ATTACHMENT_STORAGE_ENABLED", "yes")]
    [InlineData("STRATAAI_ATTACHMENT_S3_BUCKET", "public/bucket")]
    [InlineData("STRATAAI_ATTACHMENT_S3_OWNER", "123")]
    [InlineData("STRATAAI_ATTACHMENT_S3_REGION", "custom-region")]
    [InlineData("STRATAAI_ATTACHMENT_SCANNER_SOCKET", "relative")]
    [InlineData("STRATAAI_WORKER_ORGANIZATION_IDS", "")]
    [InlineData("STRATAAI_ATTACHMENT_MAX_BYTES", "-1")]
    [InlineData("STRATAAI_ATTACHMENT_MAX_BYTES", "1073741825")]
    [InlineData("STRATAAI_ATTACHMENT_ALLOWED_TYPES", "image/svg+xml")]
    [InlineData("STRATAAI_ATTACHMENT_ALLOWED_TYPES", "image/png,image/png")]
    [InlineData("STRATAAI_ATTACHMENT_ALLOWED_TYPES", "")]
    public void ARCH_12_Invalid_enabled_configuration_is_a_startup_failure(string key, string value)
    {
        var settings = Settings(); settings[key] = value;
        Assert.ThrowsAny<Exception>(() => new ServiceCollection().AddAttachmentRuntime(Config(settings), Production, worker: true));
    }

    [Fact]
    public void ARCH_06_Demo_cannot_enable_managed_storage_and_disabled_runtime_adds_no_services()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAttachmentRuntime(Config(Settings()), Production with { Mode = RuntimeMode.Demo }, false));
        var settings = Settings(); settings["STRATAAI_ATTACHMENT_STORAGE_ENABLED"] = "false";
        var services = new ServiceCollection(); Assert.False(services.AddAttachmentRuntime(Config(settings), Production, true)); Assert.Empty(services);
    }

    private sealed class Database(bool ready) : IRuntimeDependencyStatus
    {
        public RuntimeMode Mode => RuntimeMode.Production;
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(ready); }
    }

    [Fact]
    public void ARCH_12_Provider_initialization_failure_is_fixed_and_disabled_runtime_does_not_resolve_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<S3AttachmentObjectStorage>(_ => throw new InvalidOperationException("private-credential-chain-detail"));
        using var provider = services.BuildServiceProvider();
        provider.InitializeAttachmentRuntime(enabled: false);
        var error = Assert.Throws<InvalidOperationException>(() => provider.InitializeAttachmentRuntime(enabled: true));
        Assert.Equal("Attachment runtime initialization is unavailable.", error.Message); Assert.Null(error.InnerException);
    }
    private sealed class Client : AmazonS3Client
    {
        public bool Private = true; public int Checks;
        public Client() : base(new AnonymousAWSCredentials(), new AmazonS3Config { RegionEndpoint = RegionEndpoint.USEast1 }) { }
        public override Task<GetPublicAccessBlockResponse> GetPublicAccessBlockAsync(GetPublicAccessBlockRequest request, CancellationToken ct)
        {
            Checks++; ct.ThrowIfCancellationRequested();
            return Task.FromResult(new GetPublicAccessBlockResponse { PublicAccessBlockConfiguration = new()
            { BlockPublicAcls = true, IgnorePublicAcls = true, BlockPublicPolicy = true, RestrictPublicBuckets = Private } });
        }
        public override Task<GetBucketPolicyStatusResponse> GetBucketPolicyStatusAsync(GetBucketPolicyStatusRequest request, CancellationToken ct) =>
            Task.FromResult(new GetBucketPolicyStatusResponse { PolicyStatus = new() { IsPublic = false } });
        public override Task<GetBucketOwnershipControlsResponse> GetBucketOwnershipControlsAsync(GetBucketOwnershipControlsRequest request, CancellationToken ct) =>
            Task.FromResult(new GetBucketOwnershipControlsResponse { OwnershipControls = new() { Rules = [new() { ObjectOwnership = ObjectOwnership.BucketOwnerEnforced }] } });
    }

    [Fact]
    public async Task ARCH_07_Readiness_requires_current_database_and_private_bucket_and_worker_scanner()
    {
        using var client = new Client(); var storage = new S3AttachmentObjectStorage(client, "strata-private-fixture", "123456789012");
        var ct = TestContext.Current.CancellationToken;
        Assert.False(await new AttachmentRuntimeDependencyStatus(new Database(false), storage).IsReadyAsync(ct)); Assert.Equal(0, client.Checks);
        Assert.True(await new AttachmentRuntimeDependencyStatus(new Database(true), storage).IsReadyAsync(ct));
        client.Private = false;
        Assert.False(await new AttachmentRuntimeDependencyStatus(new Database(true), storage).IsReadyAsync(ct));
        client.Private = true;
        var scanner = new ClamAvAttachmentMalwareScanner("/tmp/strata-absent-" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(1));
        Assert.False(await new AttachmentRuntimeDependencyStatus(new Database(true), storage, scanner).IsReadyAsync(ct));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AttachmentRuntimeDependencyStatus(new Database(true), storage).IsReadyAsync(canceled.Token));
    }
}
