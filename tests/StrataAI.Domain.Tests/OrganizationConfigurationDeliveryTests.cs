using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationConfigurationDeliveryTests
{
    private static ClaimedBackgroundJob Claim(Guid eventId) => new(Guid.NewGuid(), Guid.NewGuid(), OrganizationConfigurationDeliveryHandler.Type,
        Guid.NewGuid(), OrganizationConfigurationDeliveryHandler.Service, "metadata-test", JsonSerializer.Serialize(new { eventId }),
        1, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));
    [Fact]
    public async Task PRD_27_Metadata_delivery_retains_exact_claim_and_reference()
    {
        var id = Guid.NewGuid(); var job = Claim(id); var store = new Store();
        await new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Same(job, store.Job); Assert.Equal(id, store.EventId); Assert.Equal(1, store.Calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"eventId\":false}")]
    [InlineData("{\"eventId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"name\":\"Private Organization\"}")]
    public async Task PRD_27_Invalid_delivery_references_never_reach_storage(string metadata)
    {
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(
            Claim(Guid.NewGuid()) with { SafeMetadataJson = metadata }, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }
    [Theory]
    [InlineData("organization")]
    [InlineData("actor")]
    [InlineData("job")]
    [InlineData("lease")]
    [InlineData("worker")]
    [InlineData("type")]
    [InlineData("service")]
    public async Task PRD_27_Invalid_delivery_scope_never_reaches_storage(string invalid)
    {
        var job = Claim(Guid.NewGuid()); var store = new Store();
        job = invalid switch {
            "organization" => job with { OrganizationId = Guid.Empty }, "actor" => job with { ActorId = Guid.Empty },
            "job" => job with { Id = Guid.Empty }, "lease" => job with { LeaseId = Guid.Empty }, "worker" => job with { WorkerId = Guid.Empty },
            "type" => job with { JobType = "OTHER" }, _ => job with { ServiceIdentity = "other" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationConfigurationDeliveryHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }
    [Fact]
    public void PRD_27_Private_duplicate_and_oversized_delivery_metadata_is_refused()
    {
        var metadata = Claim(Guid.NewGuid()).SafeMetadataJson;
        foreach (var invalid in new[] { metadata[..^1] + ",\"eventId\":null}", metadata[..^1] + ",\"name\":\"Private\"}", new string(' ', 65) })
            Assert.Throws<InvalidOperationException>(() => OrganizationLifecycleDeliveryHandler.ParseEventId(invalid));
    }
    [Fact]
    public async Task PRD_27_Unavailable_delivery_and_cancelled_dispatch_cannot_acknowledge_success()
    {
        var store = new Store { Available = false }; var handler = new OrganizationConfigurationDeliveryHandler(store); var job = Claim(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(job, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(job, cancelled.Token));
        Assert.Equal(1, store.Calls);
    }
    private sealed class Store : IOrganizationConfigurationDeliveryStore
    {
        public bool Available { get; init; } = true;
        public ClaimedBackgroundJob? Job { get; private set; }
        public Guid EventId { get; private set; }
        public int Calls { get; private set; }
        public Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken)
        { Job = job; EventId = eventId; Calls++; return Task.FromResult(Available); }
    }
}
