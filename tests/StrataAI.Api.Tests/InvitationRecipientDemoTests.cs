using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static readonly Guid DemoRecipient = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

    [Fact]
    public async Task PRD_60_Demo_recipient_replays_original_sources_across_Internal_Portal_and_Board_without_grant_leakage()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var portalResponse = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Separate Portal Organization" });
        var portal = (await portalResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var service = app.Services.GetRequiredService<IInvitationService>(); var boards = app.Services.GetRequiredService<BoardInvitationService>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>(); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var start = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct);
        Assert.True(start.Succeeded); Assert.True(start.Value!.ResetRequired); Assert.Empty(start.Value.Events);
        const string email = "demo@strataai.test";
        var internalInvite = await service.CreateAsync(f.Organization, f.Owner, email, InvitationSurface.Internal, "MEMBER", "demo-internal", ct, Guid.NewGuid());
        var portalInvite = await service.CreateAsync(portal, f.Owner, email, InvitationSurface.Portal, "OWNER", "demo-portal", ct, Guid.NewGuid());
        var boardInvite = await boards.CreateAsync(f.Board, f.Owner, email, BoardRole.Admin, "demo-board", ct, Guid.NewGuid());
        foreach (var invite in new[] { internalInvite, portalInvite, boardInvite })
        {
            Assert.True(invite.Succeeded);
            Assert.True((await service.AcceptPendingAsync(DemoRecipient, invite.Value!.Invitation.Id, "demo-accept", ct)).Succeeded);
            Assert.True((await service.AcceptPendingAsync(DemoRecipient, invite.Value.Invitation.Id, "demo-accept-retry", ct)).Succeeded);
        }
        Assert.Null(await organizations.FindMembershipAsync(portal, DemoRecipient, ct));
        Assert.True(await app.Services.GetRequiredService<IInvitationStore>().HasActivePortalAccessAsync(portal, DemoRecipient, ct));
        Assert.Equal(BoardRole.Admin, (await work.FindBoardMemberAsync(f.Board, DemoRecipient, ct))!.Role);
        var revoked = await boards.CreateAsync(f.Board, f.Owner, email, BoardRole.Member, "demo-revoke-create", ct, Guid.NewGuid());
        Assert.True(revoked.Succeeded);
        Assert.True((await boards.RevokeAsync(f.Board, f.Owner, revoked.Value!.Invitation.Id, "demo-revoke", ct)).Succeeded);
        List<InvitationRecipientEvent> events = [];
        var cursor = start.Value.Cursor;
        for (var index = 0; index < 4; index++)
        {
            var page = await replay.ReadAsync(DemoRecipient, cursor, 2, ct);
            Assert.True(page.Succeeded); Assert.False(page.Value!.ResetRequired); Assert.Equal(2, page.Value.Events.Count);
            Assert.Equal(index < 3, page.Value.HasMore); events.AddRange(page.Value.Events); cursor = page.Value.Cursor;
        }
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), events.Select(e => e.Sequence));
        Assert.Equal(new[] { "INVITATION_CREATED", "INVITATION_CREATED", "INVITATION_CREATED", "INVITATION_ACCEPTED",
            "INVITATION_ACCEPTED", "INVITATION_ACCEPTED", "INVITATION_CREATED", "INVITATION_REVOKED" }, events.Select(e => e.EventType));
        Assert.Equal(8, events.Select(e => e.EventId).Distinct().Count());
        Assert.All(events, e => Assert.NotEqual(Guid.Empty, e.EventId));
        var repeated = await replay.ReadAsync(DemoRecipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(repeated.Succeeded); Assert.Equal(events.ToArray(), repeated.Value!.Events.ToArray());
        var empty = await replay.ReadAsync(DemoRecipient, cursor, cancellationToken: ct);
        Assert.True(empty.Succeeded); Assert.Empty(empty.Value!.Events);
        var otherAccount = await replay.ReadAsync(f.Recipient, cursor, cancellationToken: ct);
        Assert.True(otherAccount.Succeeded); Assert.True(otherAccount.Value!.ResetRequired); Assert.Empty(otherAccount.Value.Events);
        var json = JsonSerializer.Serialize(events, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var wire = JsonDocument.Parse(json);
        foreach (var row in wire.RootElement.EnumerateArray())
            Assert.Equal(new[] { "createdAt", "eventId", "eventType", "sequence" }, row.EnumerateObject().Select(p => p.Name).Order());
        Assert.DoesNotContain(email, json); Assert.DoesNotContain(portal.ToString(), json);
        Assert.DoesNotContain(f.Organization.ToString(), json); Assert.DoesNotContain(internalInvite.Value!.Invitation.TokenHash, json);
        var transactions = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        // A second audit for the same committed revision must not invent another identity.
        await Assert.ThrowsAsync<InvalidOperationException>(() => transactions.ExecuteAsync(f.Organization, f.Owner, null, false, async () => {
            await organizations.AppendAuditAsync(f.Organization, DemoRecipient, "INVITATION_ACCEPTED", "Invitation", boardInvite.Value!.Invitation.Id, "duplicate-source", ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct));
        var unchanged = await replay.ReadAsync(DemoRecipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(unchanged.Succeeded); Assert.Equal(events.ToArray(), unchanged.Value!.Events.ToArray());
        var retained = await boards.CreateAsync(f.Board, f.Owner, email, BoardRole.Member, "retained-admin-create", ct);
        Assert.True(retained.Succeeded);
        Assert.True((await service.AcceptPendingAsync(DemoRecipient, retained.Value!.Invitation.Id, "retained-admin-accept", ct)).Succeeded);
        Assert.True((await service.AcceptPendingAsync(DemoRecipient, retained.Value.Invitation.Id, "retained-admin-retry", ct)).Succeeded);
        Assert.Equal(BoardRole.Admin, (await work.FindBoardMemberAsync(f.Board, DemoRecipient, ct))!.Role);
        var retainedPage = await replay.ReadAsync(DemoRecipient, cursor, cancellationToken: ct);
        Assert.True(retainedPage.Succeeded);
        Assert.Equal(new[] { "INVITATION_CREATED", "INVITATION_ACCEPTED" }, retainedPage.Value!.Events.Select(e => e.EventType));
        Assert.Equal(new long[] { 9, 10 }, retainedPage.Value.Events.Select(e => e.Sequence));
    }

    [Theory]
    [InlineData("INTERNAL")]
    [InlineData("PORTAL")]
    [InlineData("BOARD")]
    public async Task PRD_60_Demo_recipient_revocation_rolls_back_published_history_and_counter_at_final_actor_fence(string surface)
    {
        var ct = TestContext.Current.CancellationToken; var fence = new InvitationAcceptanceActorFixture();
        await using var app = new ApiFactory(configureServices: s => s.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var service = app.Services.GetRequiredService<IInvitationService>(); var boards = app.Services.GetRequiredService<BoardInvitationService>();
        var store = app.Services.GetRequiredService<IInvitationStore>(); var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var start = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct); Assert.True(start.Succeeded);
        var created = surface == "BOARD" ? await boards.CreateAsync(f.Board, f.Owner, "demo@strataai.test", BoardRole.Member, "revoke-fixture", ct)
            : await service.CreateAsync(f.Organization, f.Owner, "demo@strataai.test", surface == "PORTAL" ? InvitationSurface.Portal : InvitationSurface.Internal,
                surface == "PORTAL" ? "OWNER" : "MEMBER", "revoke-fixture", ct);
        Assert.True(created.Succeeded); var original = created.Value!.Invitation;
        fence.Admission = async (actor, token) => actor != f.Owner || (await store.FindByIdAsync(f.Organization, original.Id, token))?.RevokedAt is null;
        Task<InvitationOperation<bool>> Revoke() => surface == "BOARD"
            ? boards.RevokeAsync(f.Board, f.Owner, original.Id, "revoke-fixture", ct)
            : service.RevokeAsync(f.Organization, f.Owner, original.Id, "revoke-fixture", ct);
        var refused = await Revoke(); Assert.False(refused.Succeeded); Assert.Equal("session_unavailable", refused.ErrorCode);
        Assert.Equal(original, await store.FindByIdAsync(f.Organization, original.Id, ct));
        fence.Admission = null;
        var failedPage = await replay.ReadAsync(DemoRecipient, start.Value!.Cursor, cancellationToken: ct);
        Assert.True(failedPage.Succeeded); Assert.Equal("INVITATION_CREATED", Assert.Single(failedPage.Value!.Events).EventType);
        Assert.True((await Revoke()).Succeeded);
        var committed = await replay.ReadAsync(DemoRecipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(committed.Succeeded);
        Assert.Equal(new[] { "INVITATION_CREATED", "INVITATION_REVOKED" }, committed.Value!.Events.Select(e => e.EventType));
        Assert.Equal(new long[] { 1, 2 }, committed.Value.Events.Select(e => e.Sequence));
        Assert.Equal((await store.FindByIdAsync(f.Organization, original.Id, ct))!.RevokedAt, committed.Value.Events[1].CreatedAt);
    }

    [Fact]
    public async Task PRD_60_Demo_recipient_late_invalid_correlation_rolls_back_invitation_proof_receipt_and_counter()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var service = app.Services.GetRequiredService<IInvitationService>(); var store = app.Services.GetRequiredService<IInvitationStore>();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>(); var key = Guid.NewGuid();
        var start = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct); Assert.True(start.Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(f.Organization, f.Owner, "demo@strataai.test",
            InvitationSurface.Portal, "OWNER", new string('x', 65), ct, key));
        Assert.Empty(await store.ListPendingForEmailAsync("DEMO@STRATAAI.TEST", DateTimeOffset.UtcNow, null, ct));
        Assert.Null(await store.FindCreationReplayAsync(f.Organization, f.Owner, key, ct));
        var failed = await replay.ReadAsync(DemoRecipient, start.Value!.Cursor, cancellationToken: ct);
        Assert.True(failed.Succeeded); Assert.False(failed.Value!.ResetRequired); Assert.Empty(failed.Value.Events);
        var created = await service.CreateAsync(f.Organization, f.Owner, "demo@strataai.test", InvitationSurface.Portal, "OWNER", "valid-correlation", ct, key);
        Assert.True(created.Succeeded);
        var committed = await replay.ReadAsync(DemoRecipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(committed.Succeeded); Assert.Equal(1, Assert.Single(committed.Value!.Events).Sequence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_60_Demo_recipient_insufficient_or_inactive_Board_grant_cannot_publish_acceptance(bool inactive)
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var boards = app.Services.GetRequiredService<BoardInvitationService>(); var service = app.Services.GetRequiredService<IInvitationService>();
        var store = app.Services.GetRequiredService<IInvitationStore>(); var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var transactions = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var start = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct); Assert.True(start.Succeeded);
        var created = await boards.CreateAsync(f.Board, f.Owner, "demo@strataai.test", BoardRole.Admin, "invalid-grant-create", ct);
        Assert.True(created.Succeeded); var original = created.Value!.Invitation;
        await Assert.ThrowsAsync<InvalidOperationException>(() => transactions.ExecuteAsync(f.Organization, f.Owner, DemoRecipient, false, async () => {
            Assert.True((await store.AcceptAsync(original.TokenHash, DemoRecipient, "DEMO@STRATAAI.TEST", DateTimeOffset.UtcNow, ct)).Succeeded);
            if (inactive) Assert.True(await work.RemoveBoardMemberAsync(f.Board, DemoRecipient, DateTimeOffset.UtcNow, ct));
            else await work.UpsertBoardMemberAsync(f.Board, DemoRecipient, BoardRole.Member, DateTimeOffset.UtcNow, ct);
            await organizations.AppendAuditAsync(f.Organization, DemoRecipient, "INVITATION_ACCEPTED", "Invitation", original.Id, "invalid-grant-accept", ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct));
        Assert.Equal(original, await store.FindByIdAsync(f.Organization, original.Id, ct));
        Assert.Null(await organizations.FindMembershipAsync(f.Organization, DemoRecipient, ct));
        Assert.Null(await work.FindBoardMemberAsync(f.Board, DemoRecipient, ct));
        var failed = await replay.ReadAsync(DemoRecipient, start.Value!.Cursor, cancellationToken: ct);
        Assert.True(failed.Succeeded); Assert.Equal("INVITATION_CREATED", Assert.Single(failed.Value!.Events).EventType);
        Assert.True((await service.AcceptPendingAsync(DemoRecipient, original.Id, "valid-grant-accept", ct)).Succeeded);
        var committed = await replay.ReadAsync(DemoRecipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(committed.Succeeded); Assert.Equal(new long[] { 1, 2 }, committed.Value!.Events.Select(e => e.Sequence));
    }

    [Fact]
    public async Task PRD_60_Demo_recipient_legacy_row_has_no_invented_creation_history_but_future_actual_acceptance_publishes()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IInvitationStore>(); var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var clock = app.Services.GetRequiredService<StrataAI.Application.Common.IClock>();
        var raw = new InvitationRecord(Guid.NewGuid(), f.Organization, "demo@strataai.test", "DEMO@STRATAAI.TEST",
            new string('a', 64), InvitationSurface.Internal, "MEMBER", f.Owner, clock.UtcNow, clock.UtcNow.AddDays(7), null, null);
        await store.CreateAsync(raw, ct);
        var start = await replay.ReadAsync(DemoRecipient, null, cancellationToken: ct); Assert.True(start.Succeeded); Assert.Empty(start.Value!.Events);
        var accepted = await app.Services.GetRequiredService<IInvitationService>().AcceptPendingAsync(DemoRecipient, raw.Id, "legacy-future-accept", ct);
        Assert.True(accepted.Succeeded);
        var page = await replay.ReadAsync(DemoRecipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(page.Succeeded); var source = Assert.Single(page.Value!.Events);
        Assert.Equal("INVITATION_ACCEPTED", source.EventType); Assert.Equal(1, source.Sequence);
    }

    [Fact]
    public async Task PRD_60_Demo_recipient_reader_refuses_unowned_and_substituted_email_reads()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        var reader = app.Services.GetRequiredService<IInvitationRecipientEventReader>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetScopeAsync(DemoRecipient, ct));
        var transactions = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => transactions.ExecuteAsync(DemoRecipient, async () => {
            var scope = (await reader.GetScopeAsync(DemoRecipient, ct))!;
            await reader.GetHeadAsync(scope with { EmailNormalized = "FOREIGN@EXAMPLE.TEST" }, ct);
            return IdentityOperation<bool>.Success(true);
        }, ct));
    }
}
