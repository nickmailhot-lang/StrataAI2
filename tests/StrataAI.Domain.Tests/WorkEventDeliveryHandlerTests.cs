using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class WorkEventDeliveryHandlerTests
{
    private static ClaimedBackgroundJob Job(string metadata) => new(Guid.NewGuid(), Guid.NewGuid(),
        WorkEventDeliveryHandler.Type, Guid.NewGuid(), WorkEventDeliveryHandler.Service, "event-test", metadata,
        1, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));

    [Fact]
    public async Task DeliveryPreservesTheClaimedLeaseAndReferences()
    {
        var board = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var job = Job(JsonSerializer.Serialize(new { boardId = board, eventId }));
        var store = new Store();
        await new WorkEventDeliveryHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Same(job, store.Job);
        Assert.Equal((board, eventId), store.References);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"boardId\":null,\"eventId\":null}")]
    [InlineData("{\"boardId\":\"not-a-guid\",\"eventId\":\"not-a-guid\"}")]
    public async Task InvalidReferencesNeverReachTheDeliveryStore(string metadata)
    {
        var store = new Store();
        await Assert.ThrowsAnyAsync<Exception>(() => new WorkEventDeliveryHandler(store)
            .ExecuteAsync(Job(metadata), TestContext.Current.CancellationToken));
        Assert.Null(store.Job);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("service")]
    [InlineData("organization")]
    [InlineData("actor")]
    public async Task InvalidScopeNeverReachesTheDeliveryStore(string invalid)
    {
        var job = Job(JsonSerializer.Serialize(new { boardId = Guid.NewGuid(), eventId = Guid.NewGuid() }));
        job = invalid switch
        {
            "type" => job with { JobType = "OTHER_JOB" },
            "service" => job with { ServiceIdentity = "other-service" },
            "organization" => job with { OrganizationId = Guid.Empty },
            _ => job with { ActorId = Guid.Empty }
        };
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkEventDeliveryHandler(store)
            .ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Null(store.Job);
    }

    [Fact]
    public async Task MissingEventOrLostLeaseCannotBeAcknowledgedAsSuccess()
    {
        var store = new Store { Ready = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkEventDeliveryHandler(store)
            .ExecuteAsync(Job(JsonSerializer.Serialize(new { boardId = Guid.NewGuid(), eventId = Guid.NewGuid() })),
                TestContext.Current.CancellationToken));
    }

    private sealed class Store : IWorkEventDeliveryStore
    {
        public bool Ready { get; init; } = true;
        public ClaimedBackgroundJob? Job { get; private set; }
        public (Guid Board, Guid Event) References { get; private set; }
        public Task<bool> MarkReadyAsync(ClaimedBackgroundJob job, Guid boardId, Guid eventId, CancellationToken cancellationToken = default)
        {
            Job = job; References = (boardId, eventId);
            return Task.FromResult(Ready);
        }
    }
}
