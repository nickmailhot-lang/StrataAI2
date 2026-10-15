using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Runtime;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class AttachmentReadinessRecoveryTests
{
    [Theory]
    [InlineData("database", 0)]
    [InlineData("unsafe", 1)]
    [InlineData("permission", 2)]
    public async Task ARCH_08_AttachmentHttpReadinessWithdrawsAndRecoversWithoutReplacingHost(string fault, int expectedReads)
    {
        var database = new Database(); using var s3 = new Bucket();
        var dependency = new AttachmentRuntimeDependencyStatus(database, Storage(s3));
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IRuntimeDependencyStatus>(dependency));
        using var client = app.CreateClient(); Assert.Same(dependency, app.Services.GetRequiredService<IRuntimeDependencyStatus>());
        var ct = TestContext.Current.CancellationToken;
        var identity = await client.GetStringAsync("/api/runtime", ct); // Controlled Demo host, actual Production dependency checker.
        await Ready(client); var before = s3.Reads;
        database.Ready = fault != "database"; s3.Fault = fault;
        for (var attempt = 0; attempt < 2; attempt++) await NotReady(client);
        Assert.Equal(before + 2 * expectedReads, s3.Reads);
        using var alive = await client.GetAsync("/healthz", ct); Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        Assert.Equal(before + 2 * expectedReads, s3.Reads); // Liveness must not contact the provider.
        database.Ready = true; s3.Fault = "none"; await Ready(client);
        Assert.Equal(before + 2 * expectedReads + 3, s3.Reads);
        Assert.Same(dependency, app.Services.GetRequiredService<IRuntimeDependencyStatus>());
        Assert.Equal(identity, await client.GetStringAsync("/api/runtime", ct));
    }

    [Fact]
    public async Task ARCH_08_StalledBucketReadinessHasItsOwnDeadlineAndLaterRecovers()
    {
        var database = new Database(); using var s3 = new Bucket();
        var dependency = new AttachmentRuntimeDependencyStatus(database, Storage(s3));
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IRuntimeDependencyStatus>(dependency));
        using var client = app.CreateClient(); await Ready(client); var before = s3.Reads;
        s3.Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // The provider deliberately ignores cancellation. The readiness
            // boundary must finish before this independent four-second watchdog.
            await NotReady(client).WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
            Assert.True(s3.LastToken.IsCancellationRequested); Assert.Equal(before + 1, s3.Reads);
            using var alive = await client.GetAsync("/healthz", TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
            s3.Paused.TrySetResult(Private()); await s3.LatePolicyAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal(before + 1, s3.Reads); // Cancelled late continuation cannot send another provider request.
            s3.Paused = null; await Ready(client); Assert.Equal(before + 4, s3.Reads);
        }
        finally { s3.Paused?.TrySetCanceled(TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task ARCH_08_CallerCancellationEscapesInsteadOfBecomingProviderUnready()
    {
        using var s3 = new Bucket { Paused = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var dependency = new AttachmentRuntimeDependencyStatus(new Database(), Storage(s3));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            var read = dependency.IsReadyAsync(caller.Token);
            await s3.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken); caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.True(s3.LastToken.IsCancellationRequested); Assert.Equal(1, s3.Reads);
            s3.Paused.TrySetResult(Private()); await s3.LatePolicyAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal(1, s3.Reads); s3.Paused = null;
            Assert.True(await dependency.IsReadyAsync(TestContext.Current.CancellationToken)); Assert.Equal(4, s3.Reads);
        }
        finally { s3.Paused?.TrySetCanceled(TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task ARCH_08_WorkerDependencyChecksRecoverOnTheSameScannerSocketPath()
    {
        Assert.True(Socket.OSSupportsUnixDomainSockets);
        var root = Path.Combine(Path.GetTempPath(), "strata-ready-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "scanner.sock"); using var s3 = new Bucket();
        var scanner = new ClamAvAttachmentMalwareScanner(path, TimeSpan.FromSeconds(30));
        var dependency = new AttachmentRuntimeDependencyStatus(new Database(), Storage(s3), scanner);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await using (var server = new Ping(path, "PONG\0")) { Assert.True(await dependency.IsReadyAsync(ct)); await server.Completed; Assert.Equal(1, server.Calls); }
            Assert.False(await dependency.IsReadyAsync(ct)); // Socket has disappeared.
            await using (var server = new Ping(path, "NOPE\0")) { Assert.False(await dependency.IsReadyAsync(ct)); await server.Completed; }
            await using (var server = new Ping(path, null))
            {
                Assert.False(await dependency.IsReadyAsync(ct).WaitAsync(TimeSpan.FromSeconds(4), ct));
                Assert.Equal(1, server.Calls); // Outer two-second dependency budget dominates the scanner's 30 seconds.
            }
            await using (var server = new Ping(path, "PONG\0")) { Assert.True(await dependency.IsReadyAsync(ct)); await server.Completed; }
            Assert.Equal(15, s3.Reads); // All five checks retain current private-bucket admission before scanner I/O.
        }
        finally
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), Path.GetDirectoryName(Path.GetFullPath(root)));
            Assert.StartsWith("strata-ready-", Path.GetFileName(root)); Directory.Delete(root, recursive: true);
        }
    }

    private static S3AttachmentObjectStorage Storage(Bucket client) => new(client, "strata-readiness-fixture", "123456789012");
    private static GetPublicAccessBlockResponse Private(bool safe = true) => new() { PublicAccessBlockConfiguration = new()
    { BlockPublicAcls = true, IgnorePublicAcls = true, BlockPublicPolicy = true, RestrictPublicBuckets = safe } };
    private static async Task Ready(HttpClient client)
    {
        using var response = await client.GetAsync("/readyz", TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("ready", body.GetProperty("status").GetString()); Assert.Equal("production", body.GetProperty("mode").GetString());
    }
    private static async Task NotReady(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/readyz"); request.Headers.Add("X-Correlation-ID", "attachment-outage-reference");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "code", "correlationId", "message", "status" }, body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("not-ready", body.GetProperty("status").GetString()); Assert.Equal("runtime_not_ready", body.GetProperty("code").GetString());
        Assert.Equal("The service is not ready. Try again later.", body.GetProperty("message").GetString());
        Assert.Equal("attachment-outage-reference", body.GetProperty("correlationId").GetString());
        Assert.Equal("attachment-outage-reference", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.DoesNotContain("private-", body.GetRawText()); Assert.DoesNotContain("123456789012", body.GetRawText());
    }
    private sealed class Database : IRuntimeDependencyStatus
    {
        public bool Ready = true; public RuntimeMode Mode => RuntimeMode.Production;
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Ready); }
    }
    private sealed class Bucket : AmazonS3Client
    {
        public string Fault = "none"; public int Reads; public CancellationToken LastToken;
        public TaskCompletionSource<GetPublicAccessBlockResponse>? Paused;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), LatePolicyAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Bucket() : base(new AnonymousAWSCredentials(), new AmazonS3Config { RegionEndpoint = RegionEndpoint.USEast1 }) { }
        public override Task<GetPublicAccessBlockResponse> GetPublicAccessBlockAsync(GetPublicAccessBlockRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.Equal("strata-readiness-fixture", request.BucketName); Assert.Equal("123456789012", request.ExpectedBucketOwner);
            Reads++; LastToken = ct; Entered.TrySetResult(); return Paused?.Task ?? Task.FromResult(Private(Fault != "unsafe"));
        }
        public override Task<GetBucketPolicyStatusResponse> GetBucketPolicyStatusAsync(GetBucketPolicyStatusRequest request, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) LatePolicyAttempt.TrySetResult(); ct.ThrowIfCancellationRequested(); Reads++;
            if (Fault == "permission") throw new AmazonS3Exception("private-provider-configuration") { ErrorCode = "AccessDenied", StatusCode = HttpStatusCode.Forbidden };
            return Task.FromResult(new GetBucketPolicyStatusResponse { PolicyStatus = new() { IsPublic = false } });
        }
        public override Task<GetBucketOwnershipControlsResponse> GetBucketOwnershipControlsAsync(GetBucketOwnershipControlsRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reads++;
            return Task.FromResult(new GetBucketOwnershipControlsResponse { OwnershipControls = new() { Rules = [new() { ObjectOwnership = ObjectOwnership.BucketOwnerEnforced }] } });
        }
    }
    private sealed class Ping : IAsyncDisposable
    {
        private readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly string path; public int Calls; public Task Completed { get; }
        public Ping(string path, string? reply)
        {
            this.path = path; cancellation.CancelAfter(TimeSpan.FromSeconds(10)); listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1); Completed = Serve(reply);
        }
        private async Task Serve(string? reply)
        {
            using var peer = await listener.AcceptAsync(cancellation.Token); await using var stream = new NetworkStream(peer, ownsSocket: false);
            var request = new byte[6]; await stream.ReadExactlyAsync(request, cancellation.Token); Assert.Equal("zPING\0", Encoding.ASCII.GetString(request)); Calls++;
            if (reply is null) await Task.Delay(Timeout.Infinite, cancellation.Token);
            else await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), cancellation.Token);
        }
        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel(); listener.Dispose();
            try { await Completed; } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { cancellation.Dispose(); File.Delete(path); }
        }
    }
}
