using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Organizations;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationOperationalLifecycleDeliveryTests
{
    private static ClaimedBackgroundJob Claim(Guid eventId) => new(Guid.NewGuid(), Guid.NewGuid(),
        OrganizationOperationalLifecycleDeliveryHandler.Type, Guid.NewGuid(),
        OrganizationOperationalLifecycleDeliveryHandler.Service, "operational-lifecycle-test",
        JsonSerializer.Serialize(new { eventId }), 1, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));

    [Fact]
    public async Task PRD_27_FR007_Delivery_preserves_exact_owning_claim_and_event_reference()
    {
        var eventId = Guid.NewGuid(); var job = Claim(eventId); var store = new Store();
        await new OrganizationOperationalLifecycleDeliveryHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Same(job, store.Job); Assert.Equal(eventId, store.EventId); Assert.Equal(1, store.Calls);
    }

    [Theory]
    [InlineData("deletion")]
    [InlineData("configuration")]
    [InlineData("mixed-service")]
    [InlineData("empty-organization")]
    [InlineData("empty-actor")]
    [InlineData("empty-job")]
    [InlineData("empty-lease")]
    [InlineData("empty-worker")]
    public async Task PRD_27_FR007_Unrelated_or_incomplete_capabilities_cannot_deliver(string invalid)
    {
        var job = Claim(Guid.NewGuid()); var store = new Store();
        job = invalid switch
        {
            "deletion" => job with { JobType = OrganizationLifecycleDeliveryHandler.Type, ServiceIdentity = OrganizationLifecycleDeliveryHandler.Service },
            "configuration" => job with { JobType = OrganizationConfigurationDeliveryHandler.Type, ServiceIdentity = OrganizationConfigurationDeliveryHandler.Service },
            "mixed-service" => job with { ServiceIdentity = OrganizationLifecycleDeliveryHandler.Service },
            "empty-organization" => job with { OrganizationId = Guid.Empty },
            "empty-actor" => job with { ActorId = Guid.Empty }, "empty-job" => job with { Id = Guid.Empty },
            "empty-lease" => job with { LeaseId = Guid.Empty }, _ => job with { WorkerId = Guid.Empty },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationOperationalLifecycleDeliveryHandler(store)
            .ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task PRD_27_FR007_Private_duplicate_or_malformed_references_never_reach_storage()
    {
        var job = Claim(Guid.NewGuid()); var store = new Store(); var metadata = job.SafeMetadataJson;
        foreach (var invalid in new[] { "{}", "[]", "null", "{broken", metadata[..^1] + ",\"name\":\"Private\"}",
            metadata[..^1] + ",\"eventId\":null}", "{\"eventId\":\"00000000-0000-0000-0000-000000000000\"}", new string(' ', 65) })
            await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationOperationalLifecycleDeliveryHandler(store)
                .ExecuteAsync(job with { SafeMetadataJson = invalid }, TestContext.Current.CancellationToken));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task PRD_27_FR007_Unavailable_source_cannot_acknowledge_delivery()
    {
        var store = new Store { Available = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationOperationalLifecycleDeliveryHandler(store)
            .ExecuteAsync(Claim(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task PRD_27_FR007_Cancellation_before_or_during_delivery_cannot_acknowledge()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var store = new Store();
        var handler = new OrganizationOperationalLifecycleDeliveryHandler(store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(Claim(Guid.NewGuid()), cancelled.Token));
        Assert.Equal(0, store.Calls);
        using var during = new CancellationTokenSource(); store.BeforeReturn = during.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(Claim(Guid.NewGuid()), during.Token));
        Assert.Equal(1, store.Calls);
    }

    private sealed class Store : IOrganizationOperationalLifecycleDeliveryStore
    {
        public bool Available { get; init; } = true;
        public Action? BeforeReturn { get; set; }
        public ClaimedBackgroundJob? Job { get; private set; }
        public Guid EventId { get; private set; }
        public int Calls { get; private set; }
        public Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid eventId, CancellationToken cancellationToken)
        { Job = job; EventId = eventId; Calls++; BeforeReturn?.Invoke(); return Task.FromResult(Available); }
    }
}
