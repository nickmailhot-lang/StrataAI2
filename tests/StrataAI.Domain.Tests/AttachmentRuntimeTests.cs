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

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ARCH_07_Local_attachment_fixture_is_rejected_outside_IntegrationTest(string? environment)
    {
        var settings = Settings(); settings["STRATAAI_ATTACHMENT_TEST_LOCAL_ROOT"] = Path.GetTempPath();
        settings["ASPNETCORE_ENVIRONMENT"] = environment;
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAttachmentRuntime(Config(settings), Production, false, environment));
        Assert.Equal("Local attachment fixtures require the IntegrationTest environment.", error.Message);
    }

    [Fact]
    public void ARCH_07_Explicit_local_fixture_registers_storage_without_managed_credentials_and_rejects_conflicting_host_environment()
    {
        var settings = Settings(); settings["STRATAAI_ATTACHMENT_TEST_LOCAL_ROOT"] = Path.GetTempPath();
        settings["ASPNETCORE_ENVIRONMENT"] = "IntegrationTest";
        settings.Remove("STRATAAI_ATTACHMENT_S3_BUCKET"); settings.Remove("STRATAAI_ATTACHMENT_S3_OWNER"); settings.Remove("STRATAAI_ATTACHMENT_S3_REGION");
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAttachmentRuntime(Config(settings), Production, false, "Production"));
        var services = new ServiceCollection(); Assert.True(services.AddAttachmentRuntime(Config(settings), Production, false, "IntegrationTest"));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAttachmentObjectStorage));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAmazonS3) || descriptor.ServiceType == typeof(S3AttachmentObjectStorage));
        settings["DOTNET_ENVIRONMENT"] = "Production";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAttachmentRuntime(Config(settings), Production, false, "IntegrationTest"));
    }

    [Fact]
    public async Task ARCH_07_Worker_wiring_registers_managed_storage_scanner_and_exact_handler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AttachmentPreviewWorkerProcess("/usr/share/dotnet/dotnet", "/app/StrataAI.Worker.dll"));
        services.AddSingleton<IAmazonS3>(new Client());
        services.AddSingleton(_ => new PostgresConnectionFactory("Host=localhost;Database=fixture;Username=fixture;Password=fixture"));
        Assert.True(services.AddAttachmentRuntime(Config(Settings()), Production, worker: true));
        await using var provider = services.BuildServiceProvider();
        Assert.IsType<S3AttachmentObjectStorage>(provider.GetRequiredService<IAttachmentObjectStorage>());
        Assert.IsType<ClamAvAttachmentMalwareScanner>(provider.GetRequiredService<IAttachmentMalwareScanner>());
        Assert.IsType<PostgresAttachmentScanDeliveryStore>(provider.GetRequiredService<IAttachmentScanDeliveryStore>());
        var handlers=provider.GetServices<IBackgroundJobHandler>().ToArray(); Assert.Equal(2,handlers.Length);
        var handler = Assert.IsType<AttachmentScanDeliveryHandler>(Assert.Single(handlers,h=>h.JobType==AttachmentScanJobs.Type));
        Assert.Equal(AttachmentScanJobs.Type, handler.JobType); Assert.Equal(AttachmentScanJobs.Service, handler.ServiceIdentity);
        Assert.NotNull(provider.GetRequiredService<AttachmentQuarantineScanner>());
        Assert.IsType<LinuxIsolatedAttachmentImagePreviewGenerator>(provider.GetRequiredService<IAttachmentImagePreviewGenerator>());
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IAttachmentImagePreviewDecoder));
        Assert.IsType<PrivateAttachmentDownloadPreparer>(provider.GetRequiredService<IAttachmentDownloadPreparer>());
        var preview=Assert.IsType<AttachmentPreviewDeliveryHandler>(Assert.Single(handlers,h=>h.JobType==AttachmentPreviewJobs.Type));
        Assert.Equal(AttachmentPreviewJobs.Service,preview.ServiceIdentity);
        Assert.Same(provider.GetRequiredService<IAttachmentPreviewIntentStore>(),provider.GetRequiredService<IAttachmentPreviewPublicationStore>());
        Assert.IsType<PostgresAttachmentPreviewIntentStore>(provider.GetRequiredService<IAttachmentPreviewIntentStore>());
        Assert.IsType<PostgresAttachmentPreviewBackfillStore>(provider.GetRequiredService<IAttachmentPreviewBackfillStore>());
        Assert.IsType<PostgresAttachmentScanRecoveryStore>(provider.GetRequiredService<IAttachmentScanRecoveryStore>());
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
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IAttachmentImagePreviewDecoder));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IAttachmentImagePreviewGenerator));
        Assert.DoesNotContain(services,s=>s.ServiceType==typeof(IAttachmentPreviewIntentStore) || s.ServiceType==typeof(IAttachmentPreviewPublicationStore) || s.ServiceType==typeof(AttachmentPreviewStorageRecovery));
        Assert.DoesNotContain(services,s=>s.ServiceType==typeof(IAttachmentPreviewBackfillStore));
        Assert.DoesNotContain(services,s=>s.ServiceType==typeof(IAttachmentScanRecoveryStore));
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

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task ARCH_06_Demo_cannot_enable_managed_storage_and_disabled_runtime_composes_without_providers(string? flag)
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAttachmentRuntime(Config(Settings()), Production with { Mode = RuntimeMode.Demo }, false));
        foreach (var mode in new[] { RuntimeMode.Demo, RuntimeMode.Production })
        foreach (var worker in new[] { false, true })
        {
            var settings = Settings(); settings["STRATAAI_ATTACHMENT_STORAGE_ENABLED"] = flag;
            var services = new ServiceCollection();
            Assert.False(services.AddAttachmentRuntime(Config(settings), Production with { Mode = mode }, worker));
            Assert.DoesNotContain(services, s => s.ServiceType == typeof(IAmazonS3)
                || s.ServiceType == typeof(S3AttachmentObjectStorage) || s.ServiceType == typeof(IAttachmentMalwareScanner)
                || s.ServiceType == typeof(IBackgroundJobHandler) || s.ServiceType == typeof(PostgresConnectionFactory));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var storage = Assert.IsType<UnavailableAttachmentObjectStorage>(provider.GetRequiredService<IAttachmentObjectStorage>());
            var reference = new AttachmentObjectReference(Guid.NewGuid(), Guid.NewGuid());
            using var bytes = new MemoryStream([1, 2, 3]);
            foreach (var operation in new Func<Task>[] {
                () => storage.WritePrivateAsync(reference, bytes, 1024, CancellationToken.None),
                () => storage.OpenPrivateReadAsync(reference, CancellationToken.None),
                () => storage.DeletePrivateAsync(reference, CancellationToken.None) })
            {
                var error = await Assert.ThrowsAsync<AttachmentStorageException>(operation);
                Assert.Equal("object_storage_unavailable", error.Code);
            }
            Assert.Equal(0, bytes.Position);
            var preparer = provider.GetRequiredService<IAttachmentDownloadPreparer>();
            var request = new AttachmentScanRequest(reference, 3, new string('a', 64));
            Assert.Null(await preparer.PrepareAsync(request, CancellationToken.None));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.OpenPrivateReadAsync(reference, cancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparer.PrepareAsync(request, cancelled.Token));
            Assert.NotNull(provider.GetRequiredService<AttachmentUploadPolicy>());
            Assert.NotNull(provider.GetRequiredService<IAttachmentFileTypeInspector>());
            provider.InitializeAttachmentRuntime(enabled: false);
        }
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
        services.AddSingleton<IAttachmentObjectStorage>(provider => provider.GetRequiredService<S3AttachmentObjectStorage>());
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
