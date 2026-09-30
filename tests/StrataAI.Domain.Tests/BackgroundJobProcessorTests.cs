using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;

namespace StrataAI.Domain.Tests;

public sealed class BackgroundJobProcessorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static ClaimedBackgroundJob Job() => new(Guid.NewGuid(), Guid.NewGuid(), "TEST_JOB", Guid.NewGuid(),
        "test-service", "arch-07-test", "{}", 1, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(2));

    [Fact]
    public async Task ARCH_07_TC_01_EmptyQueueDoesNotDispatchOrAcknowledge()
    {
        var store = new Store(null);
        var handler = new Handler();
        Assert.Equal(JobProcessingResult.Empty, await Processor(store, handler).ProcessOneAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, store.Completions);
    }

    [Fact]
    public async Task ARCH_07_TC_01_SuccessUsesTheClaimedLeaseAndWorker()
    {
        var job = Job(); var store = new Store(job); var handler = new Handler();
        Assert.Equal(JobProcessingResult.Completed, await Processor(store, handler).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Same(job, handler.Received);
        Assert.Equal((job.OrganizationId, job.Id, job.LeaseId, job.WorkerId), store.Acknowledged);
        Assert.Null(store.ErrorCode);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("worker")]
    [InlineData("actor")]
    public async Task ARCH_07_AC_004_InvalidClaimScopeCannotExecute(string invalid)
    {
        var original = Job();
        var job = invalid == "actor" ? original with { ActorId = Guid.Empty } : original;
        var store = new Store(job); var handler = new Handler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Processor(store, handler).ProcessOneAsync(
            invalid == "organization" ? Guid.NewGuid() : job.OrganizationId,
            invalid == "worker" ? Guid.NewGuid() : job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, store.Completions);
        Assert.Null(store.ErrorCode);
    }

    [Theory]
    [InlineData("UNKNOWN_JOB", "test-service", "job_handler_missing")]
    [InlineData("TEST_JOB", "another-service", "job_service_denied")]
    public async Task ARCH_07_TC_01_UnknownTypeOrServiceCannotDispatch(string type, string service, string code)
    {
        var job = Job() with { JobType = type, ServiceIdentity = service };
        var store = new Store(job); var handler = new Handler();
        Assert.Equal(JobProcessingResult.Retried, await Processor(store, handler).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(code, store.ErrorCode);
    }

    [Fact]
    public async Task ARCH_07_TC_01_ProviderFailureStoresOnlyASafeCode()
    {
        var job = Job(); var store = new Store(job);
        var handler = new Handler { Execute = _ => Task.FromException(new InvalidOperationException("provider-password-secret")) };
        Assert.Equal(JobProcessingResult.Retried, await Processor(store, handler).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal("job_handler_failed", store.ErrorCode);
        Assert.Equal(0, store.Completions);
    }

    [Fact]
    public async Task ARCH_07_TC_01_ShutdownLeavesLeaseForRecovery()
    {
        var job = Job(); var store = new Store(job); using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handler = new Handler { Execute = token => { stop.Cancel(); return Task.FromCanceled(token); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(store, handler).ProcessOneAsync(job.OrganizationId, job.WorkerId, stop.Token));
        Assert.Equal(0, store.Completions); Assert.Null(store.ErrorCode);
    }

    [Fact]
    public async Task ARCH_07_TC_01_ExpiredLeaseCannotStartAnEffect()
    {
        var job = Job() with { LeaseExpiresAt = Now.AddSeconds(4) };
        var store = new Store(job); var handler = new Handler();
        Assert.Equal(JobProcessingResult.LeaseLost, await Processor(store, handler).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ARCH_07_TC_01_RejectedAcknowledgementIsNotReportedAsSuccess()
    {
        var job = Job(); var store = new Store(job) { AcceptAcknowledgement = false };
        Assert.Equal(JobProcessingResult.LeaseLost, await Processor(store, new Handler()).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARCH_07_TC_01_ExecutionDeadlineCancelsAndRetriesWithoutAcknowledging()
    {
        var job = Job() with { LeaseExpiresAt = Now.AddSeconds(5.1) };
        var store = new Store(job);
        var handler = new Handler { Execute = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        Assert.Equal(JobProcessingResult.Retried, await Processor(store, handler).ProcessOneAsync(job.OrganizationId, job.WorkerId, TestContext.Current.CancellationToken));
        Assert.Equal("job_execution_timeout", store.ErrorCode);
        Assert.Equal(0, store.Completions);
    }

    [Fact]
    public void ARCH_07_TC_01_DuplicateHandlersFailDeployment()
    {
        Assert.Throws<ArgumentException>(() => new BackgroundJobProcessor(new Store(null), new Clock(), [new Handler(), new Handler()]));
    }

    private static BackgroundJobProcessor Processor(Store store, Handler handler) => new(store, new Clock(), [handler]);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Handler : IBackgroundJobHandler
    {
        public string JobType => "TEST_JOB";
        public string ServiceIdentity => "test-service";
        public int Calls { get; private set; }
        public ClaimedBackgroundJob? Received { get; private set; }
        public Func<CancellationToken, Task> Execute { get; init; } = _ => Task.CompletedTask;
        public Task ExecuteAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
        { Calls++; Received = job; return Execute(cancellationToken); }
    }
    private sealed class Store(ClaimedBackgroundJob? job) : IBackgroundJobStore
    {
        public int Completions { get; private set; }
        public string? ErrorCode { get; private set; }
        public bool AcceptAcknowledgement { get; init; } = true;
        public (Guid, Guid, Guid, Guid)? Acknowledged { get; private set; }
        public Task<ClaimedBackgroundJob?> ClaimAsync(Guid organizationId, Guid workerId, CancellationToken cancellationToken = default) => Task.FromResult(job);
        public Task<bool> CompleteAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, CancellationToken cancellationToken = default)
        { Completions++; Acknowledged = (organizationId, jobId, leaseId, workerId); return Task.FromResult(AcceptAcknowledgement); }
        public Task<bool> FailAsync(Guid organizationId, Guid jobId, Guid leaseId, Guid workerId, string errorCode, CancellationToken cancellationToken = default)
        { ErrorCode = errorCode; return Task.FromResult(AcceptAcknowledgement); }
    }
}
