using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_03_Demo_terminal_discovery_withdraws_pending_and_deleted_graphs_but_preserves_private_recovery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var peer = app.CreateClient();
        var f = await NotificationFixture(app, owner, peer, ct);
        using var controlResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Still active control" });
        Assert.Equal(HttpStatusCode.Created, controlResponse.StatusCode);
        var control = (await controlResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        async Task<Guid[]> Directory(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/organizations", ct))
            .EnumerateArray().Select(row => row.GetProperty("organization").GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(f.Organization, await Directory(owner)); Assert.Contains(f.Organization, await Directory(peer));
        var request = Guid.NewGuid();
        using var accepted = await Mutate(owner, HttpMethod.Delete, $"/organizations/{f.Organization}?version=1", new { }, request.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(new[] { control }, await Directory(owner)); Assert.Empty(await Directory(peer));
        Assert.True(await app.Services.GetRequiredService<IDemoOrganizationDeletionSimulation>().AdvanceAsync(ct));
        Assert.Equal(new[] { control }, await Directory(owner)); Assert.Empty(await Directory(peer));
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        Assert.True((await organizations.FindMembershipAsync(f.Organization, f.Recipient, ct))!.Active);
        Assert.Equal(OrganizationStatus.Deleted, (await organizations.FindOrganizationAsync(f.Organization, ct))!.Status);
        using var active = await owner.GetAsync($"/organizations/{control}", ct); Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        using var observation = await owner.GetAsync($"/organizations/{f.Organization}/deletion-requests/{request}", ct);
        Assert.Equal("COMPLETED", (await observation.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString());
        using var lifecycle = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
        Assert.Equal("COMPLETED", (await lifecycle.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task PRD_03_Demo_terminal_completes_actual_archived_graph_and_recovers_one_original_source_and_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var peer = app.CreateClient();
        var f = await NotificationFixture(app, owner, peer, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var attachments = app.Services.GetRequiredService<IAttachmentMetadataStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>(); var simulator = app.Services.GetRequiredService<IDemoOrganizationDeletionSimulation>();
        var at = DateTimeOffset.UtcNow; List<CardRecord> cards = [];
        for (var i = 0; i < 130; i++)
        {
            var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Retained archived child", "Private description", null, at, ct);
            cards.Add((await work.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, at.AddSeconds(1), ct))!);
        }
        var attachment = await attachments.CreateUrlAttachmentAsync(Guid.NewGuid(), f.Organization, cards[0].Id, f.Owner,
            "Historical attachment", "https://example.test/retained", at, ct);
        using var active = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
        Assert.Equal("ACTIVE", (await active.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString());
        var request = Guid.NewGuid(); var delete = $"/organizations/{f.Organization}?version=1&expectedActorId={f.Owner}";
        using var accepted = await Mutate(owner, HttpMethod.Delete, delete, new { }, request.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await simulator.AdvanceAsync(ct)); Assert.False(await simulator.AdvanceAsync(ct));
        var parent = (await organizations.FindOrganizationAsync(f.Organization, ct))!;
        Assert.Equal(OrganizationStatus.Deleted, parent.Status); Assert.Equal(3, parent.Version);
        foreach (var original in cards)
        {
            var tombstone = (await work.FindCardAsync(original.Id, ct, includeDeleted: true))!;
            Assert.Equal(WorkItemLifecycleState.Deleted, tombstone.LifecycleState); Assert.Equal(f.Owner, tombstone.DeletedBy);
            Assert.Equal(original.ArchivedAt, tombstone.ArchivedAt); Assert.Equal(original.Description, tombstone.Description);
        }
        Assert.Equal(WorkItemLifecycleState.Deleted, (await work.FindListAsync(f.List, ct, includeDeleted: true))!.LifecycleState);
        Assert.Equal(BoardLifecycleState.Deleted, (await work.FindBoardAsync(f.Board, ct, includeDeleted: true))!.LifecycleState);
        Assert.Equal(AttachmentLifecycleState.Deleted, (await attachments.FindLifecycleAttachmentAsync(f.Organization, attachment.CardId, attachment.Id, ct))!.LifecycleState);
        using var observed = await owner.GetAsync($"/organizations/{f.Organization}/deletion-requests/{request}", ct);
        var observation = (await observed.Content.ReadFromJsonAsync<JsonElement>(ct));
        Assert.Equal("COMPLETED", observation.GetProperty("state").GetString()); Assert.Equal(3, observation.GetProperty("version").GetInt64());
        using var lifecycle = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events?expectedActorId={f.Recipient}", ct);
        Assert.Equal(HttpStatusCode.OK, lifecycle.StatusCode); Assert.Contains("no-store", lifecycle.Headers.CacheControl!.ToString());
        var page = (await lifecycle.Content.ReadFromJsonAsync<JsonElement>(ct)); Assert.Equal("COMPLETED", page.GetProperty("state").GetString());
        var source = Assert.Single(page.GetProperty("events").EnumerateArray());
        Assert.Equal(new[] { "actorId", "boardId", "createdAt", "entityId", "entityType", "eventId", "eventType", "metadata", "organizationId", "version" },
            source.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("ORGANIZATION_DELETED", source.GetProperty("eventType").GetString()); Assert.Equal(f.Owner, source.GetProperty("actorId").GetGuid());
        Assert.Equal(observation.GetProperty("eventId").GetGuid(), source.GetProperty("eventId").GetGuid());
        Assert.Equal(parent.UpdatedAt, source.GetProperty("createdAt").GetDateTimeOffset()); Assert.Equal(JsonValueKind.Null, source.GetProperty("boardId").ValueKind);
        Assert.Empty(source.GetProperty("metadata").EnumerateObject());
        using var replay = await Mutate(owner, HttpMethod.Delete, delete, new { }, request.ToString()); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        using var recovered = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
        Assert.Equal(page.ToString(), (await recovered.Content.ReadFromJsonAsync<JsonElement>(ct)).ToString());
        using var privateRequest = await peer.GetAsync($"/organizations/{f.Organization}/deletion-requests/{request}", ct);
        Assert.Equal(HttpStatusCode.NotFound, privateRequest.StatusCode);
        using var ordinary = await peer.GetAsync($"/organizations/{f.Organization}", ct); Assert.Equal(HttpStatusCode.NotFound, ordinary.StatusCode);
        await organizations.RemoveMemberAsync(f.Organization, f.Recipient, DateTimeOffset.UtcNow, ct);
        using var withdrawn = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct); Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
    }

    [Fact]
    public async Task PRD_03_Demo_terminal_automatic_host_finishes_committed_request_after_owner_logout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(demoDeletionDispatch: true); using var owner = app.CreateClient(); using var peer = app.CreateClient();
        var f = await NotificationFixture(app, owner, peer, ct);
        var request = Guid.NewGuid();
        using var accepted = await Mutate(owner, HttpMethod.Delete, $"/organizations/{f.Organization}?version=1", new { }, request.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var logout = await Mutate(owner, HttpMethod.Post, "/auth/logout", new { }); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        JsonElement page = default;
        for (var i = 0; i < 100; i++)
        {
            using var response = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (page.GetProperty("state").GetString() == "COMPLETED") break;
            await Task.Delay(100, ct);
        }
        Assert.Equal("COMPLETED", page.GetProperty("state").GetString()); Assert.Single(page.GetProperty("events").EnumerateArray());
        using var unavailable = await owner.GetAsync($"/organizations/{f.Organization}/deletion-requests/{request}", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unavailable.StatusCode);
    }

    [Fact]
    public async Task PRD_03_Demo_terminal_preserves_committed_actor_after_account_deactivation_and_membership_withdrawal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var peer = app.CreateClient();
        var f = await NotificationFixture(app, owner, peer, ct); var request = Guid.NewGuid();
        using var accepted = await Mutate(owner, HttpMethod.Delete, $"/organizations/{f.Organization}?version=1", new { }, request.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(await app.Services.GetRequiredService<IIdentityStore>().DeactivateUserAsync(f.Owner, DateTimeOffset.UtcNow, ct));
        await app.Services.GetRequiredService<IOrganizationStore>().RemoveMemberAsync(f.Organization, f.Owner, DateTimeOffset.UtcNow, ct);
        Assert.True(await app.Services.GetRequiredService<IDemoOrganizationDeletionSimulation>().AdvanceAsync(ct));
        using var response = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("COMPLETED", page.GetProperty("state").GetString());
        Assert.Equal(f.Owner, Assert.Single(page.GetProperty("events").EnumerateArray()).GetProperty("actorId").GetGuid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_03_Demo_terminal_late_publication_failure_or_cancellation_rolls_back_graph_parent_audit_and_ready_source(bool cancel)
    {
        var ct = TestContext.Current.CancellationToken; using var stopped = CancellationTokenSource.CreateLinkedTokenSource(ct);
        TerminalPublicationFailure? failure = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(d => d.ServiceType == typeof(IDemoOrganizationDeletionCompletionPublisher));
            services.AddSingleton<IDemoOrganizationDeletionCompletionPublisher>(provider => failure = new(
                (IDemoOrganizationDeletionCompletionPublisher)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), stopped, cancel));
        });
        using var owner = app.CreateClient(); using var peer = app.CreateClient(); var f = await NotificationFixture(app, owner, peer, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Atomic terminal child", null, null, DateTimeOffset.UtcNow, ct);
        var request = Guid.NewGuid();
        using var accepted = await Mutate(owner, HttpMethod.Delete, $"/organizations/{f.Organization}?version=1", new { }, request.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var simulator = app.Services.GetRequiredService<IDemoOrganizationDeletionSimulation>();
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => simulator.AdvanceAsync(stopped.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => simulator.AdvanceAsync(ct));
        Assert.Equal(card, await work.FindCardAsync(card.Id, ct));
        Assert.Equal(OrganizationStatus.Deleting, (await app.Services.GetRequiredService<IOrganizationStore>().FindOrganizationAsync(f.Organization, ct))!.Status);
        using var pending = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
        var page = await pending.Content.ReadFromJsonAsync<JsonElement>(ct); Assert.Equal("PENDING", page.GetProperty("state").GetString());
        Assert.Empty(page.GetProperty("events").EnumerateArray());
        failure!.Armed = false; Assert.True(await simulator.AdvanceAsync(ct)); Assert.False(await simulator.AdvanceAsync(ct));
        using var completed = await peer.GetAsync($"/organizations/{f.Organization}/lifecycle-events", ct);
        Assert.Equal("COMPLETED", (await completed.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString());
    }

    private sealed class TerminalPublicationFailure(IDemoOrganizationDeletionCompletionPublisher inner,
        CancellationTokenSource stop, bool cancel) : IDemoOrganizationDeletionCompletionPublisher
    {
        public bool Armed { get; set; } = true;
        public async Task PublishAsync(Guid organizationId, Guid actorId, Guid requestId,
            OrganizationMetadataEvent source, CancellationToken cancellationToken = default)
        {
            await inner.PublishAsync(organizationId, actorId, requestId, source, cancellationToken);
            if (!Armed) return;
            if (cancel) stop.Cancel(); else throw new InvalidOperationException("After actual terminal publication.");
        }
    }
}
