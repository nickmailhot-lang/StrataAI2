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

    // Audit state is internal infrastructure, not a new public diagnostics route.
    // Observe the actual stored record; successful lease responses alone cannot
    // prove refused/no-op operations preserved its clocks and revision.
    private static (DateTimeOffset Created, DateTimeOffset Updated, long Version) Audit(Fixture fixture, Guid id)
    {
        var field = typeof(InMemoryBackgroundJobStore).GetField("_rows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var rows = Assert.IsAssignableFrom<System.Collections.IDictionary>(field.GetValue(fixture.Store));
        var row = Assert.Single(rows.Values.Cast<object>(), value => ((NewBackgroundJob)value.GetType().GetProperty("Job")!.GetValue(value)!).Id == id);
        object Value(string name)
        {
            var property = row.GetType().GetProperty(name); Assert.NotNull(property);
            return property.GetValue(row)!;
        }
        return (Assert.IsType<DateTimeOffset>(Value("CreatedAt")), Assert.IsType<DateTimeOffset>(Value("UpdatedAt")), Assert.IsType<long>(Value("Version")));
    }

    [Fact]
    public async Task FOUND_FR_009_Demo_job_audit_tracks_admitted_changes_and_preserves_refusals_duplicates_and_rollback()
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var worker = Guid.NewGuid(); var job = Job(Guid.NewGuid()); var created = f.Clock.UtcNow;
        f.Publish(job); Assert.Equal((created, created, 1L), Audit(f, job.Id));
        f.Clock.UtcNow = created.AddMinutes(1); f.Publish(job with { Id = Guid.NewGuid() });
        Assert.Equal((created, created, 1L), Audit(f, job.Id));
        Assert.Null(await f.Store.ClaimAsync(Guid.NewGuid(), worker, ct));
        Assert.Equal((created, created, 1L), Audit(f, job.Id));
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.ClaimAsync(job.OrganizationId, worker, cancelled.Token));
        }
        var lease = await f.Store.ClaimAsync(job.OrganizationId, worker, ct); Assert.NotNull(lease);
        var claimed = Audit(f, job.Id); Assert.Equal((created, f.Clock.UtcNow, 2L), claimed);
        f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(1);
        Assert.False(await f.Store.CompleteAsync(job.OrganizationId, job.Id, Guid.NewGuid(), worker, ct));
        Assert.False(await f.Store.CompleteAsync(Guid.NewGuid(), job.Id, lease.LeaseId, worker, ct));
        Assert.False(await f.Store.CompleteAsync(job.OrganizationId, job.Id, lease.LeaseId, Guid.NewGuid(), ct));
        Assert.Equal(claimed, Audit(f, job.Id));
        var rollback = f.Store.CaptureRollback();
        Assert.True(await f.Store.FailAsync(job.OrganizationId, job.Id, lease.LeaseId, worker, "delivery_unavailable", ct));
        Assert.Equal((created, f.Clock.UtcNow, 3L), Audit(f, job.Id));
        rollback(); Assert.Equal(claimed, Audit(f, job.Id));
        Assert.True(await f.Store.CompleteAsync(job.OrganizationId, job.Id, lease.LeaseId, worker, ct));
        var finished = Audit(f, job.Id); Assert.Equal((created, f.Clock.UtcNow, 3L), finished);
        f.Clock.UtcNow = f.Clock.UtcNow.AddHours(1); f.Publish(job);
        Assert.False(await f.Store.CompleteAsync(job.OrganizationId, job.Id, lease.LeaseId, worker, ct));
        Assert.Null(await f.Store.ClaimAsync(job.OrganizationId, worker, ct));
        Assert.Equal(finished, Audit(f, job.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FOUND_FR_009_Demo_job_retry_and_final_expiry_have_actual_update_clocks(bool crash)
    {
        var f = new Fixture(); var ct = TestContext.Current.CancellationToken;
        var job = Job(Guid.NewGuid()); var worker = Guid.NewGuid(); var created = f.Clock.UtcNow; f.Publish(job);
        long version = 1;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(1);
            var lease = await f.Store.ClaimAsync(job.OrganizationId, worker, ct); Assert.NotNull(lease);
            Assert.Equal((created, f.Clock.UtcNow, ++version), Audit(f, job.Id));
            f.Clock.UtcNow = crash ? lease.LeaseExpiresAt : f.Clock.UtcNow.AddSeconds(1);
            if (crash)
            {
                Assert.False(await f.Store.CompleteAsync(job.OrganizationId, job.Id, lease.LeaseId, worker, ct));
                if (attempt == 5)
                {
                    Assert.Null(await f.Store.ClaimAsync(job.OrganizationId, worker, ct));
                    Assert.Equal((created, f.Clock.UtcNow, ++version), Audit(f, job.Id));
                }
            }
            else
            {
                Assert.True(await f.Store.FailAsync(job.OrganizationId, job.Id, lease.LeaseId, worker, "delivery_unavailable", ct));
                Assert.Equal((created, f.Clock.UtcNow, ++version), Audit(f, job.Id));
                f.Clock.UtcNow = f.Clock.UtcNow.AddSeconds(30 * Math.Pow(2, attempt - 1));
            }
        }
        var terminal = Audit(f, job.Id); f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1);
        Assert.Null(await f.Store.ClaimAsync(job.OrganizationId, worker, ct)); Assert.Equal(terminal, Audit(f, job.Id));
    }

}
