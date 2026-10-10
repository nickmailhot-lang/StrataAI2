using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed class DemoBackgroundJobStoreTests
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public DemoWorkTransactionScope Scope { get; } = new();
        public InMemoryAccountOrganizationGate Gate { get; } = new();
        public InMemoryBackgroundJobStore Store { get; }
        public Fixture() => Store = new(Clock, Scope, Gate);
        public void Publish(NewBackgroundJob job)
        {
            Gate.WorkCommands.Wait();
            try { using var owned = Scope.Enter(job.OrganizationId); Store.Publish(job, TestContext.Current.CancellationToken); }
            finally { Gate.WorkCommands.Release(); }
        }
    }
    private static NewBackgroundJob Job(Guid organization, string key = "first") =>
        new(Guid.NewGuid(), organization, "CARD_REMINDER", key, Guid.NewGuid(), "card-reminder-delivery", "demo-test", "{}");

    [Fact]
    public async Task ARCH_03_Demo_queue_retains_first_publication_and_tenant_worker_lease_fences()
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var org = Guid.NewGuid(); var worker = Guid.NewGuid(); var job = Job(org);
        f.Publish(job); f.Publish(job with { Id = Guid.NewGuid(), ActorId = Guid.NewGuid(), SafeMetadataJson = "{\"changed\":true}" });
        Assert.Null(await f.Store.ClaimAsync(Guid.NewGuid(), worker, ct));
        var claimed = await f.Store.ClaimAsync(org, worker, ct); Assert.NotNull(claimed);
        Assert.Equal(job.Id, claimed.Id); Assert.Equal(job.ActorId, claimed.ActorId); Assert.Equal(job.SafeMetadataJson, claimed.SafeMetadataJson);
        Assert.Equal(1, claimed.AttemptCount); Assert.Equal(f.Clock.UtcNow.AddMinutes(2), claimed.LeaseExpiresAt);
        Assert.Null(await f.Store.ClaimAsync(org, Guid.NewGuid(), ct));
        Assert.False(await f.Store.CompleteAsync(Guid.NewGuid(), job.Id, claimed.LeaseId, worker, ct));
        Assert.False(await f.Store.CompleteAsync(org, job.Id, Guid.NewGuid(), worker, ct));
        Assert.False(await f.Store.CompleteAsync(org, job.Id, claimed.LeaseId, Guid.NewGuid(), ct));
        Assert.True(await f.Store.CompleteAsync(org, job.Id, claimed.LeaseId, worker, ct));
        Assert.False(await f.Store.CompleteAsync(org, job.Id, claimed.LeaseId, worker, ct));
        f.Publish(job with { Id = Guid.NewGuid() }); Assert.Null(await f.Store.ClaimAsync(org, worker, ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARCH_03_Demo_queue_bounds_retries_and_rejects_expired_final_leases(bool crash)
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var org = Guid.NewGuid(); var worker = Guid.NewGuid(); var job = Job(org); f.Publish(job);
        ClaimedBackgroundJob? previous = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var claimed = await f.Store.ClaimAsync(org, worker, ct); Assert.NotNull(claimed); Assert.Equal(attempt, claimed.AttemptCount);
            if (previous is not null) Assert.False(await f.Store.CompleteAsync(org, job.Id, previous.LeaseId, worker, ct));
            if (crash)
            {
                f.Clock.UtcNow = claimed.LeaseExpiresAt;
                Assert.False(await f.Store.CompleteAsync(org, job.Id, claimed.LeaseId, worker, ct));
            }
            else
            {
                Assert.True(await f.Store.FailAsync(org, job.Id, claimed.LeaseId, worker, "delivery_unavailable", ct));
                Assert.Null(await f.Store.ClaimAsync(org, worker, ct));
                f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(30 * Math.Pow(2, attempt - 1));
            }
            previous = claimed;
        }
        Assert.Null(await f.Store.ClaimAsync(org, worker, ct)); f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1);
        Assert.Null(await f.Store.ClaimAsync(org, worker, ct)); f.Publish(job);
        Assert.Null(await f.Store.ClaimAsync(org, worker, ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_consumers_cannot_claim_uncommitted_publication_or_restore_over_other_tenants()
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var existing = Job(Guid.NewGuid()); f.Publish(existing);
        var worker = Guid.NewGuid(); var lease = await f.Store.ClaimAsync(existing.OrganizationId, worker, ct); Assert.NotNull(lease);
        var aborted = Job(Guid.NewGuid()); await f.Gate.WorkCommands.WaitAsync(ct);
        Task<ClaimedBackgroundJob?> pending;
        try
        {
            using var scope = f.Scope.Enter(aborted.OrganizationId);
            var rollback = f.Store.CaptureRollback(); f.Store.Publish(aborted, ct);
            pending = f.Store.ClaimAsync(aborted.OrganizationId, worker, ct);
            Assert.False(pending.IsCompleted); rollback();
        }
        finally { f.Gate.WorkCommands.Release(); }
        Assert.Null(await pending);
        Assert.True(await f.Store.CompleteAsync(existing.OrganizationId, existing.Id, lease.LeaseId, worker, ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_queue_honors_schedule_and_cancellation_without_consuming_a_lease()
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var org = Guid.NewGuid(); var worker = Guid.NewGuid();
        var job = Job(org) with { AvailableAt = f.Clock.UtcNow.AddHours(1) }; f.Publish(job);
        Assert.Null(await f.Store.ClaimAsync(org, worker, ct)); f.Clock.UtcNow = job.AvailableAt.Value;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.ClaimAsync(org, worker, cancelled.Token));
        var claimed = await f.Store.ClaimAsync(org, worker, ct); Assert.NotNull(claimed); Assert.Equal(1, claimed.AttemptCount);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.FailAsync(org, job.Id, claimed.LeaseId, worker, "unsafe provider text", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.FailAsync(org, job.Id, claimed.LeaseId, worker, "delivery_unavailable\n", ct));
        Assert.True(await f.Store.CompleteAsync(org, job.Id, claimed.LeaseId, worker, ct));
    }

    [Fact]
    public void ARCH_03_Demo_publication_refuses_missing_or_other_Organization_command_scope()
    {
        var f = new Fixture(); var job = Job(Guid.NewGuid()); var ct = TestContext.Current.CancellationToken;
        Assert.Throws<InvalidOperationException>(() => f.Store.Publish(job, ct));
        using var scope = f.Scope.Enter(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => f.Store.Publish(job, ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_concurrent_consumers_claim_each_publication_at_most_once()
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken; var job = Job(Guid.NewGuid()); f.Publish(job);
        await f.Gate.WorkCommands.WaitAsync(ct);
        Task<ClaimedBackgroundJob?>[] contenders;
        try
        {
            contenders = Enumerable.Range(0, 16).Select(_ => f.Store.ClaimAsync(job.OrganizationId, Guid.NewGuid(), ct)).ToArray();
            Assert.All(contenders, task => Assert.False(task.IsCompleted));
        }
        finally { f.Gate.WorkCommands.Release(); }
        var claims = await Task.WhenAll(contenders);
        var claimed = Assert.Single(claims, claim => claim is not null); Assert.NotNull(claimed);
        Assert.Equal(job.Id, claimed.Id); Assert.Equal(1, claimed.AttemptCount);
    }
}
