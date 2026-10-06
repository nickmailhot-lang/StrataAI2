using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationDeletionJobsTests
{
    private static OrganizationDeletionAttempt Attempt() => new(Guid.NewGuid(), Guid.NewGuid(), 2);
    private static ClaimedBackgroundJob Claim(OrganizationDeletionAttempt attempt)
    {
        var published = OrganizationDeletionJobs.Create(Guid.NewGuid(), Guid.NewGuid(), attempt, "prd-03-lifecycle");
        return new(published.Id, published.OrganizationId, published.JobType, published.ActorId,
            published.ServiceIdentity, published.CorrelationId, published.SafeMetadataJson, 1,
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));
    }
    [Fact]
    public void PRD_03_Completion_steps_have_distinct_durable_keys_and_content_free_references()
    {
        var attempt = Attempt(); var org = Guid.NewGuid(); var actor = Guid.NewGuid();
        var first = OrganizationDeletionJobs.Create(org, actor, attempt, "deletion-test");
        var replay = OrganizationDeletionJobs.Create(org, actor, attempt, "deletion-test");
        var continuation = OrganizationDeletionJobs.Create(org, actor, attempt with { StepId = Guid.NewGuid() }, "deletion-test");
        Assert.Equal(first.IdempotencyKey, replay.IdempotencyKey);
        Assert.NotEqual(first.IdempotencyKey, continuation.IdempotencyKey);
        Assert.Equal(attempt, OrganizationDeletionAttempt.Parse(first.SafeMetadataJson));
        using var doc = JsonDocument.Parse(first.SafeMetadataJson);
        Assert.Equal(new[] { "acceptedVersion", "requestId", "stepId" }, doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(org, first.OrganizationId); Assert.Equal(actor, first.ActorId);
    }
    [Fact]
    public async Task PRD_03_Completion_preserves_claimed_lease_and_uses_one_bounded_page()
    {
        var attempt = Attempt(); var job = Claim(attempt); var store = new Store();
        await new OrganizationDeletionPageHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Same(job, store.Job); Assert.Equal(attempt, store.Attempt); Assert.Equal(128, store.PageSize); Assert.Equal(1, store.Calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{broken")]
    public async Task PRD_03_Invalid_completion_references_never_reach_storage(string metadata)
    {
        var store = new Store(); var job = Claim(Attempt()) with { SafeMetadataJson = metadata };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationDeletionPageHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }
    [Fact]
    public async Task PRD_03_Private_or_duplicate_completion_metadata_is_refused()
    {
        var job = Claim(Attempt()); var store = new Store();
        foreach (var extra in new[] { ",\"name\":\"Private Organization\"", ",\"requestId\":\"00000000-0000-0000-0000-000000000000\"" })
        {
            var bad = job with { SafeMetadataJson = job.SafeMetadataJson[..^1] + extra + "}" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationDeletionPageHandler(store).ExecuteAsync(bad, TestContext.Current.CancellationToken));
        }
        Assert.Equal(0, store.Calls);
    }
    [Theory]
    [InlineData("organization")]
    [InlineData("actor")]
    [InlineData("lease")]
    [InlineData("worker")]
    [InlineData("type")]
    [InlineData("service")]
    public async Task PRD_03_Invalid_completion_scope_never_reaches_storage(string invalid)
    {
        var job = Claim(Attempt()); var store = new Store();
        job = invalid switch
        {
            "organization" => job with { OrganizationId = Guid.Empty },
            "actor" => job with { ActorId = Guid.Empty },
            "lease" => job with { LeaseId = Guid.Empty },
            "worker" => job with { WorkerId = Guid.Empty },
            "type" => job with { JobType = "OTHER" },
            _ => job with { ServiceIdentity = "other-service" }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationDeletionPageHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void PRD_03_Invalid_accepted_versions_cannot_publish_or_dispatch(long version)
    {
        var attempt = Attempt() with { AcceptedVersion = version };
        Assert.Throws<InvalidOperationException>(() => OrganizationDeletionJobs.Create(Guid.NewGuid(), Guid.NewGuid(), attempt, "test"));
        Assert.Throws<InvalidOperationException>(() => OrganizationDeletionAttempt.Parse(JsonSerializer.Serialize(new
            { requestId = attempt.RequestId, stepId = attempt.StepId, acceptedVersion = version })));
    }
    [Fact]
    public async Task PRD_03_Lost_completion_lease_or_preexisting_cancellation_is_not_success()
    {
        var store = new Store { Available = false }; var handler = new OrganizationDeletionPageHandler(store); var job = Claim(Attempt());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(job, TestContext.Current.CancellationToken));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(job, canceled.Token));
        Assert.Equal(1, store.Calls);
    }
    private sealed class Store : IOrganizationDeletionPageStore
    {
        public bool Available { get; init; } = true;
        public int Calls { get; private set; }
        public ClaimedBackgroundJob? Job { get; private set; }
        public OrganizationDeletionAttempt? Attempt { get; private set; }
        public int PageSize { get; private set; }
        public Task<bool> ApplyPageAsync(ClaimedBackgroundJob job, OrganizationDeletionAttempt attempt, int pageSize, CancellationToken cancellationToken)
        { Calls++; Job = job; Attempt = attempt; PageSize = pageSize; return Task.FromResult(Available); }
    }
}
