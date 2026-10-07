using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-03-TC-01/06/07/09: Demo uses the ordinary HTTP/cursor contracts.
    [Fact]
    public async Task Demo_metadata_replays_canonical_edits_once_without_reapplying_an_old_receipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(environment: "Development");
        using var owner = app.CreateClient(); using var outsider = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(outsider);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var org = await CreateDemoMetadataOrganization(owner);
        var initial = await ReadDemoMetadataPage(owner, org);
        Assert.True(initial.ResetRequired); Assert.Empty(initial.Events);
        var organizationStore = app.Services.GetRequiredService<IOrganizationStore>();
        var createdRecord = (await organizationStore.FindOrganizationAsync(org, ct))!;
        var ownerGrant = (await organizationStore.FindMembershipAsync(org, actor, ct))!;
        var creationCursor = app.Services.GetRequiredService<IOrganizationMetadataCursorCodec>().Encode(new(org, actor, ownerGrant.Id, ownerGrant.Version), 0);
        var creation = Assert.Single((await ReadDemoMetadataPage(owner, org, creationCursor)).Events);
        Assert.Equal("ORGANIZATION_CREATED", creation.EventType); Assert.Equal(1, creation.Version);
        Assert.Equal(createdRecord.CreatedAt, creation.CreatedAt); Assert.Equal(org, creation.EntityId); Assert.Equal(actor, creation.ActorId);
        var key = Guid.NewGuid().ToString(); var body = new { name = "First canonical edit", version = 1 };
        using var edited = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var acknowledgment = await edited.Content.ReadFromJsonAsync<OrganizationRecord>(ct);
        var page = await ReadDemoMetadataPage(owner, org, initial.Cursor);
        var item = Assert.Single(page.Events);
        Assert.Equal("ORGANIZATION_UPDATED", item.EventType); Assert.Equal(actor, item.ActorId);
        Assert.Equal(org, item.OrganizationId); Assert.Equal(org, item.EntityId); Assert.Equal("Organization", item.EntityType);
        Assert.Equal(2, item.Version); Assert.Equal(acknowledgment!.UpdatedAt, item.CreatedAt);
        Assert.NotEqual(Guid.Empty, item.EventId); Assert.Null(item.BoardId); Assert.Empty(item.Metadata);
        Assert.False(page.ResetRequired); Assert.False(page.Pending); Assert.False(page.HasMore);
        using var later = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", new { name = "Later canonical edit", version = 2 });
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        using var replay = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", body, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(acknowledgment, await replay.Content.ReadFromJsonAsync<OrganizationRecord>(ct));
        var history = await ReadDemoMetadataPage(owner, org, initial.Cursor);
        Assert.Equal(2, history.Events.Count); Assert.Equal(JsonSerializer.Serialize(item), JsonSerializer.Serialize(history.Events[0])); Assert.Equal(3, history.Events[1].Version);
        var firstPage = await ReadDemoMetadataPage(owner, org, initial.Cursor, 1);
        Assert.True(firstPage.HasMore); Assert.Equal(item.EventId, Assert.Single(firstPage.Events).EventId);
        var secondPage = await ReadDemoMetadataPage(owner, org, firstPage.Cursor, 1);
        Assert.False(secondPage.HasMore); Assert.Equal(3, Assert.Single(secondPage.Events).Version);
        var invalid = await ReadDemoMetadataPage(owner, org, "invalid-protected-cursor");
        Assert.True(invalid.ResetRequired); Assert.Empty(invalid.Events);
        Assert.Equal("Later canonical edit", (await app.Services.GetRequiredService<IOrganizationStore>().FindOrganizationAsync(org, ct))!.Name);
        var other = await CreateDemoMetadataOrganization(owner);
        var wrongScope = await ReadDemoMetadataPage(owner, other, initial.Cursor);
        Assert.True(wrongScope.ResetRequired); Assert.Empty(wrongScope.Events);
        using var denied = await outsider.GetAsync($"/organizations/{org}/metadata-events", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.DoesNotContain("canonical", await denied.Content.ReadAsStringAsync(ct));
        using var wrongActor = await owner.GetAsync($"/organizations/{org}/metadata-events?expectedActorId={Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongActor.StatusCode);
        using var invalidLimit = await owner.GetAsync($"/organizations/{org}/metadata-events?limit=101", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        var reader = app.Services.GetRequiredService<IOrganizationMetadataEventReader>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetHeadAsync(org, ct));
    }

    // PRD-03-TC-04/05/07/08: all Internal sources and membership cursor fences.
    [Fact]
    public async Task Demo_metadata_membership_and_invitation_sources_preserve_versions_and_exclude_Portal_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(); using var owner = app.CreateClient(); using var member = app.CreateClient();
        await RegisterAndLogin(owner); await RegisterAndLogin(member);
        var user = await member.GetFromJsonAsync<JsonElement>("/me", ct);
        var actor = user.GetProperty("id").GetGuid(); var email = user.GetProperty("email").GetString();
        var org = await CreateDemoMetadataOrganization(owner); var start = await ReadDemoMetadataPage(owner, org);
        async Task<Guid> Invite(string surface = "INTERNAL", string role = "MEMBER")
        {
            using var response = await Mutate(owner, HttpMethod.Post, $"/organizations/{org}/invitations", new { email, surface, targetRole = role });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        }
        var first = await Invite();
        using var accepted = await Mutate(member, HttpMethod.Post, $"/me/invitations/{first}/accept", new { });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var grant = (await store.FindMembershipAsync(org, actor, ct))!;
        var rows = await ReadDemoMetadataPage(owner, org, start.Cursor);
        Assert.Equal(new[] { "ORGANIZATION_MEMBER_INVITED", "INVITATION_ACCEPTED", "ORGANIZATION_MEMBER_ADDED" }, rows.Events.Select(e => e.EventType));
        var added = rows.Events[2]; Assert.Equal(grant.Id, added.EntityId); Assert.Equal(grant.Version, added.Version);
        Assert.Equal(grant.UpdatedAt, added.CreatedAt); Assert.Equal(actor, added.ActorId);
        var invitation = (await app.Services.GetRequiredService<IInvitationStore>().FindByIdAsync(org, first, ct))!;
        Assert.Equal(invitation.CreatedAt, rows.Events[0].CreatedAt); Assert.Equal(invitation.AcceptedAt, rows.Events[1].CreatedAt);
        Assert.Equal(1, rows.Events[0].Version); Assert.Equal(2, rows.Events[1].Version);
        var memberCursor = (await ReadDemoMetadataPage(member, org)).Cursor;
        var removalKey = Guid.NewGuid().ToString();
        using var removed = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{actor}?expectedVersion={grant.Version}", new { }, removalKey);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var removalReplay = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/members/{actor}?expectedVersion={grant.Version}", new { }, removalKey);
        Assert.Equal(HttpStatusCode.NoContent, removalReplay.StatusCode);
        var removal = Assert.Single((await ReadDemoMetadataPage(owner, org, rows.Cursor)).Events);
        var inactive = (await store.FindMembershipAsync(org, actor, ct))!;
        Assert.Equal("ORGANIZATION_MEMBER_REMOVED", removal.EventType); Assert.Equal(grant.Id, removal.EntityId);
        Assert.Equal(inactive.Version, removal.Version); Assert.Equal(inactive.UpdatedAt, removal.CreatedAt);
        using var denied = await member.GetAsync($"/organizations/{org}/metadata-events?cursor={Uri.EscapeDataString(memberCursor)}", ct);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var restored = await Invite();
        using var rejoined = await Mutate(member, HttpMethod.Post, $"/me/invitations/{restored}/accept", new { });
        Assert.Equal(HttpStatusCode.OK, rejoined.StatusCode);
        var changedBinding = await ReadDemoMetadataPage(member, org, memberCursor);
        Assert.True(changedBinding.ResetRequired); Assert.Empty(changedBinding.Events);
        var beforeLeave = await ReadDemoMetadataPage(owner, org);
        using var left = await Mutate(member, HttpMethod.Post, $"/organizations/{org}/leave", new { });
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        var departure = Assert.Single((await ReadDemoMetadataPage(owner, org, beforeLeave.Cursor)).Events);
        Assert.Equal("ORGANIZATION_MEMBER_REMOVED", departure.EventType); Assert.Equal(actor, departure.ActorId);
        Assert.Equal(grant.Id, departure.EntityId);
        var revoke = await Invite();
        var beforeRevoke = await ReadDemoMetadataPage(owner, org);
        using var revoked = await Mutate(owner, HttpMethod.Delete, $"/organizations/{org}/invitations/{revoke}", new { });
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var revokedEvent = Assert.Single((await ReadDemoMetadataPage(owner, org, beforeRevoke.Cursor)).Events);
        Assert.Equal("INVITATION_REVOKED", revokedEvent.EventType); Assert.Equal(revoke, revokedEvent.EntityId); Assert.Equal(2, revokedEvent.Version);
        var beforePortal = await ReadDemoMetadataPage(owner, org);
        await Invite("PORTAL", "OWNER");
        Assert.Empty((await ReadDemoMetadataPage(owner, org, beforePortal.Cursor)).Events);
        using var boardResponse = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = org, name = "Separate Board source" });
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = (await boardResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var boardInvitation = await Mutate(owner, HttpMethod.Post, $"/boards/{board}/invitations", new { email, role = "MEMBER" });
        Assert.Equal(HttpStatusCode.Created, boardInvitation.StatusCode);
        var boardInvite = (await boardInvitation.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var boardRevoke = await Mutate(owner, HttpMethod.Delete, $"/boards/{board}/invitations/{boardInvite}", new { });
        Assert.Equal(HttpStatusCode.NoContent, boardRevoke.StatusCode);
        Assert.Empty((await ReadDemoMetadataPage(owner, org, beforePortal.Cursor)).Events);
    }

    // PRD-03-TC-08: a waiting reader cannot see a tentative publication.
    [Fact]
    public async Task Demo_metadata_waiting_reader_observes_restored_history_after_Organization_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture()));
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var org = await CreateDemoMetadataOrganization(owner); var start = await ReadDemoMetadataPage(owner, org);
        var store = app.Services.GetRequiredService<IOrganizationStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refused = unit.ExecuteAsync(org, actor, null, false, async () => {
            await store.UpdateOrganizationAsync(org, "Tentative streamed edit", null, null, 1, DateTimeOffset.UtcNow, ct);
            await store.AppendAuditAsync(org, actor, "ORGANIZATION_UPDATED", "Organization", org, "demo-metadata-queued-read", ct);
            entered.SetResult(); await release.Task.WaitAsync(ct);
            return OrganizationOperation<bool>.Failure("fixture_refused");
        }, ct);
        await entered.Task.WaitAsync(ct);
        var queued = app.Services.GetRequiredService<TransactionalOrganizationMetadataSynchronization>().ReadAsync(org, actor, start.Cursor, cancellationToken: ct);
        Assert.False(queued.IsCompleted);
        release.SetResult(); Assert.False((await refused.WaitAsync(ct)).Succeeded);
        var page = await queued.WaitAsync(ct);
        Assert.True(page.Succeeded); Assert.Empty(page.Value!.Events); Assert.False(page.Value.ResetRequired);
        Assert.Equal("Demo metadata parent", (await store.FindOrganizationAsync(org, ct))!.Name);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("actor")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task Demo_metadata_rolls_back_the_source_and_sequence_after_a_late_command_refusal(string outcome)
    {
        var ct = TestContext.Current.CancellationToken; var fence = new OrganizationTransactionActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        var org = await CreateDemoMetadataOrganization(owner); var start = await ReadDemoMetadataPage(owner, org);
        var store = app.Services.GetRequiredService<IOrganizationStore>(); var original = (await store.FindOrganizationAsync(org, ct))!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<OrganizationOperation<bool>> Operation()
        {
            await store.UpdateOrganizationAsync(org, "Tentative private edit", null, null, 1, DateTimeOffset.UtcNow, ct);
            await store.AppendAuditAsync(org, actor, "ORGANIZATION_UPDATED", "Organization", org, "demo-metadata-refusal", ct);
            if (outcome == "actor") fence.Allowed = false;
            if (outcome == "exception") throw new InvalidOperationException("fixture refusal after metadata source publication");
            if (outcome == "cancel") cancellation.Cancel();
            return outcome == "failure" ? OrganizationOperation<bool>.Failure("fixture_refused") : OrganizationOperation<bool>.Success(true);
        }
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        if (outcome == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(org, actor, null, false, Operation, cancellation.Token));
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(org, actor, null, false, Operation, cancellation.Token));
        else Assert.False((await unit.ExecuteAsync(org, actor, null, false, Operation, cancellation.Token)).Succeeded);
        fence.Allowed = true;
        Assert.Equal(original, await store.FindOrganizationAsync(org, ct));
        Assert.Empty((await ReadDemoMetadataPage(owner, org, start.Cursor)).Events);
        using var fresh = await Mutate(owner, HttpMethod.Patch, $"/organizations/{org}", new { name = "Fresh committed edit", version = 1 });
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        var page = await ReadDemoMetadataPage(owner, org, start.Cursor);
        Assert.False(page.ResetRequired); Assert.False(page.Pending); Assert.Equal(2, Assert.Single(page.Events).Version);
    }

    private static async Task<Guid> CreateDemoMetadataOrganization(HttpClient client)
    {
        using var created = await Mutate(client, HttpMethod.Post, "/organizations", new { name = "Demo metadata parent" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("organization").GetProperty("id").GetGuid();
    }
    private sealed record DemoMetadataEvent(Guid EventId, string EventType, Guid ActorId, Guid OrganizationId,
        long Version, DateTimeOffset CreatedAt, string EntityType, Guid EntityId, Guid? BoardId, IReadOnlyDictionary<string, string> Metadata);
    private sealed record DemoMetadataPage(string Cursor, bool HasMore, bool Pending, bool ResetRequired, IReadOnlyList<DemoMetadataEvent> Events);
    private static async Task<DemoMetadataPage> ReadDemoMetadataPage(HttpClient client, Guid org, string? cursor = null, int limit = 50)
    {
        using var response = await client.GetAsync($"/organizations/{org}/metadata-events?limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DemoMetadataPage>(TestContext.Current.CancellationToken))!;
    }
}
