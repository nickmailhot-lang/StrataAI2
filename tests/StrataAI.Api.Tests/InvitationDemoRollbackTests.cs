using System.Net.Http.Json;
using System.Text.Json;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Onboarding;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(false, "actor")]
    [InlineData(true, "actor")]
    [InlineData(false, "exception")]
    [InlineData(true, "exception")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    public async Task PRD_03_05_Demo_invitation_creation_rolls_back_rows_receipts_and_Board_events_before_exact_key_retry(bool boardInvitation, string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fence = new OrganizationTransactionActorFixture();
        var publisher = new InvitationRollbackPublisher(fence, cancellation);
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<StrataAI.Application.Identity.ICommandActorAuthorization>(fence);
            services.AddSingleton<IInvitationMailPublisher>(publisher);
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var history = app.Services.GetRequiredService<IInvitationHistoryStore>();
        var organizationService = app.Services.GetRequiredService<IInvitationService>();
        var boardService = app.Services.GetRequiredService<BoardInvitationService>();
        var events = app.Services.GetRequiredService<IWorkEventReader>();
        var key = Guid.NewGuid();
        var recipient = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        const string email = "demo@strataai.test";
        var replayReader = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var replayStart = await replayReader.ReadAsync(recipient, null, cancellationToken: ct);
        Assert.True(replayStart.Succeeded); Assert.Empty(replayStart.Value!.Events);
        var before = await events.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        publisher.Outcome = outcome;
        Task<InvitationOperation<CreatedInvitation>> Create(CancellationToken token) => boardInvitation
            ? boardService.CreateAsync(f.Board, f.Owner, email, BoardRole.Member, "fixture", token, key)
            : organizationService.CreateAsync(f.Organization, f.Owner, email, InvitationSurface.Internal, "MEMBER", "fixture", token, key);
        if (outcome == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => Create(cancellation.Token));
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(cancellation.Token));
        else
        {
            var refused = await Create(cancellation.Token);
            Assert.False(refused.Succeeded); Assert.Equal("session_unavailable", refused.ErrorCode);
        }
        Assert.Equal(1, publisher.Attempts);
        Assert.Empty(await history.ListAsync(f.Organization, null, ct, boardInvitation ? f.Board : null));
        Assert.Empty(await invitations.ListPendingForEmailAsync(email.ToUpperInvariant(), DateTimeOffset.UtcNow, null, ct));
        Assert.Null(await invitations.FindCreationReplayAsync(f.Organization, f.Owner, key, ct));
        var after = await events.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Equal(before.Cursor, after.Cursor);
        Assert.Equal(before.Events.Select(row => row.Event).ToArray(), after.Events.Select(row => row.Event).ToArray());
        fence.Allowed = true; publisher.Outcome = "success";
        var rolledBack = await replayReader.ReadAsync(recipient, replayStart.Value.Cursor, cancellationToken: ct);
        Assert.True(rolledBack.Succeeded); Assert.False(rolledBack.Value!.ResetRequired); Assert.Empty(rolledBack.Value.Events);
        var created = await Create(ct);
        Assert.True(created.Succeeded);
        Assert.Equal(2, publisher.Attempts);
        var committed = await replayReader.ReadAsync(recipient, replayStart.Value.Cursor, cancellationToken: ct);
        Assert.True(committed.Succeeded);
        var source = Assert.Single(committed.Value!.Events);
        Assert.Equal("INVITATION_CREATED", source.EventType); Assert.Equal(1, source.Sequence);
        Assert.Equal(created.Value!.Invitation.CreatedAt, source.CreatedAt);
        var retained = Assert.Single(await history.ListAsync(f.Organization, null, ct, boardInvitation ? f.Board : null));
        Assert.Equal(created.Value!.Invitation.Id, retained.Id);
        var receipt = (await invitations.FindCreationReplayAsync(f.Organization, f.Owner, key, ct))!;
        Assert.Equal(retained.Id, receipt.Invitation.Id);
        // The failed command must not poison this key; the successful command's
        // immutable receipt then prevents another invitation/publication.
        var replay = await Create(ct);
        Assert.True(replay.Succeeded); Assert.Equal(created.Value.Invitation.Id, replay.Value!.Invitation.Id);
        Assert.Equal(2, publisher.Attempts);
        Assert.Single(await history.ListAsync(f.Organization, null, ct, boardInvitation ? f.Board : null));
        var repeated = await replayReader.ReadAsync(recipient, replayStart.Value.Cursor, cancellationToken: ct);
        Assert.True(repeated.Succeeded); Assert.Equal(committed.Value.Events, repeated.Value!.Events);
    }

    [Theory]
    [InlineData("INTERNAL")]
    [InlineData("PORTAL")]
    [InlineData("BOARD")]
    public async Task PRD_03_05_Demo_acceptance_restores_token_and_grants_after_post_consumption_actor_refusal(string surface)
    {
        var ct = TestContext.Current.CancellationToken;
        var fence = new InvitationAcceptanceActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(fence));
        using var owner = app.CreateClient(); using var member = app.CreateClient(); using var recipientClient = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        await RegisterAndLogin(recipientClient);
        var profile = await recipientClient.GetFromJsonAsync<JsonElement>("/me", ct);
        var recipient = profile.GetProperty("id").GetGuid(); var email = profile.GetProperty("email").GetString()!;
        var invitations = app.Services.GetRequiredService<IInvitationStore>();
        var service = app.Services.GetRequiredService<IInvitationService>();
        var replayReader = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        var start = await replayReader.ReadAsync(recipient, null, cancellationToken: ct);
        Assert.True(start.Succeeded); Assert.Empty(start.Value!.Events);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var events = app.Services.GetRequiredService<IWorkEventReader>();
        var created = surface == "BOARD"
            ? await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(f.Board, f.Owner, email, BoardRole.Member, "fixture", ct)
            : await service.CreateAsync(f.Organization, f.Owner, email, surface == "PORTAL" ? InvitationSurface.Portal : InvitationSurface.Internal,
                surface == "PORTAL" ? "OWNER" : "MEMBER", "fixture", ct);
        Assert.True(created.Succeeded); Assert.NotEmpty(created.Value!.RawToken);
        var original = created.Value.Invitation;
        var before = await events.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        fence.Admission = async (actor, token) => actor != recipient
            || (await invitations.FindByIdAsync(f.Organization, original.Id, token))?.AcceptedAt is null;
        var refused = await service.AcceptAsync(recipient, created.Value.RawToken, "fixture", ct);
        Assert.False(refused.Succeeded); Assert.Equal("session_unavailable", refused.ErrorCode);
        Assert.Equal(original, await invitations.FindByIdAsync(f.Organization, original.Id, ct));
        Assert.Null(await organizations.FindMembershipAsync(f.Organization, recipient, ct));
        Assert.False(await invitations.HasActivePortalAccessAsync(f.Organization, recipient, ct));
        Assert.Null(await work.FindBoardMemberAsync(f.Board, recipient, ct));
        var after = await events.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Equal(before.Cursor, after.Cursor);
        Assert.Equal(before.Events.Select(row => row.Event).ToArray(), after.Events.Select(row => row.Event).ToArray());
        fence.Admission = null;
        var rolledBack = await replayReader.ReadAsync(recipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(rolledBack.Succeeded); Assert.False(rolledBack.Value!.ResetRequired);
        Assert.Equal("INVITATION_CREATED", Assert.Single(rolledBack.Value.Events).EventType);
        Assert.True((await service.AcceptAsync(recipient, created.Value.RawToken, "fixture", ct)).Succeeded);
        Assert.Equal(recipient, (await invitations.FindByIdAsync(f.Organization, original.Id, ct))!.AcceptedByUserId);
        Assert.Equal(surface != "PORTAL", (await organizations.FindMembershipAsync(f.Organization, recipient, ct))?.Active == true);
        Assert.Equal(surface == "PORTAL", await invitations.HasActivePortalAccessAsync(f.Organization, recipient, ct));
        Assert.Equal(surface == "BOARD", (await work.FindBoardMemberAsync(f.Board, recipient, ct))?.Active == true);
        Assert.False((await service.AcceptAsync(recipient, created.Value.RawToken, "fixture", ct)).Succeeded);
        var committed = await replayReader.ReadAsync(recipient, start.Value.Cursor, cancellationToken: ct);
        Assert.True(committed.Succeeded); Assert.False(committed.Value!.ResetRequired);
        Assert.Equal(new[] { "INVITATION_CREATED", "INVITATION_ACCEPTED" }, committed.Value.Events.Select(e => e.EventType));
        Assert.Equal(new long[] { 1, 2 }, committed.Value.Events.Select(e => e.Sequence));
        Assert.Equal(2, committed.Value.Events.Select(e => e.EventId).Distinct().Count());
    }

    private sealed class InvitationAcceptanceActorFixture : ICommandActorAuthorization
    {
        public Func<Guid, CancellationToken, Task<bool>>? Admission { get; set; }
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Admission?.Invoke(actorId, cancellationToken) ?? Task.FromResult(true); }
    }

    private sealed class InvitationRollbackPublisher(OrganizationTransactionActorFixture fence,
        CancellationTokenSource cancellation) : IInvitationMailPublisher
    {
        public string Outcome { get; set; } = "success";
        public int Attempts { get; private set; }
        public InvitationDeliveryToken CreateToken(Guid organizationId, Guid invitationId) => new(Guid.NewGuid().ToString("N"), "fixture-key");
        public Task PublishAsync(InvitationRecord invitation, string keyId, string correlationId, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Outcome == "actor") fence.Allowed = false;
            if (Outcome == "exception") throw new InvalidOperationException("Fixture publication refused after invitation insertion.");
            if (Outcome == "cancel") cancellation.Cancel();
            return Task.CompletedTask;
        }
    }
}
