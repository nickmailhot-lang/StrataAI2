using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
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
    [Fact]
    public async Task Invitation_recipient_live_withholds_page_when_account_revision_changes_after_session_read()
    {
        var ct = TestContext.Current.CancellationToken; RecipientRevisionBoundary? boundary = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(s => s.ServiceType == typeof(IInvitationRecipientEventReader));
            services.AddSingleton<IInvitationRecipientEventReader>(p => boundary = new RecipientRevisionBoundary(
                (IInvitationRecipientEventReader)original.ImplementationFactory!(p), p.GetRequiredService<IIdentityStore>()));
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        using var recipient = app.CreateClient(); var cookie = await RegisterAndLogin(recipient);
        var profile = await recipient.GetFromJsonAsync<JsonElement>("/me", ct); var actor = profile.GetProperty("id").GetGuid();
        var email = profile.GetProperty("email").GetString()!;
        var cursor = app.Services.GetRequiredService<IInvitationRecipientCursorCodec>().Encode(
            new(actor, email.ToUpperInvariant(), profile.GetProperty("version").GetInt64()), 0);
        Assert.True((await app.Services.GetRequiredService<IInvitationService>().CreateAsync(f.Organization, f.Owner,
            email, InvitationSurface.Internal, "MEMBER", "revision-boundary", ct)).Succeeded);
        using var socket = await LiveSocket(app, cookie, "/invitations/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new[] { cursor } });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while (true)
            {
                var frame = await Frame(socket, deadline.Token);
                if (frame is null || frame.Value.TryGetProperty("type", out var type) && type.GetInt32() is 3 or 7) break;
                Assert.NotEqual(2, frame.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { }
        Assert.NotNull(boundary); Assert.True(boundary.Changed);
    }

    private sealed class RecipientRevisionBoundary(IInvitationRecipientEventReader inner, IIdentityStore identities)
        : IInvitationRecipientEventReader
    {
        private int scopes;
        public bool Changed { get; private set; }
        public async Task<InvitationRecipientCursorBinding?> GetScopeAsync(Guid actorId, CancellationToken ct)
        {
            // Replay's two scope reads complete first. Change the revision at
            // the hub's final binding check, after its fresh session lookup.
            if (Interlocked.Increment(ref scopes) == 3)
            {
                var user = await identities.FindUserByIdAsync(actorId, ct); Assert.NotNull(user);
                var updated = await identities.UpdateProfileAsync(actorId, "Changed at delivery", user.AvatarUrl,
                    user.Locale, user.Timezone, user.Version, DateTimeOffset.UtcNow, ct);
                Assert.NotNull(updated); Changed = true;
            }
            return await inner.GetScopeAsync(actorId, ct);
        }
        public Task<long> GetHeadAsync(InvitationRecipientCursorBinding binding, CancellationToken ct) => inner.GetHeadAsync(binding, ct);
        public Task<InvitationRecipientEventWindow> ReadAsync(InvitationRecipientCursorBinding binding, long since, int limit, CancellationToken ct)
            => inner.ReadAsync(binding, since, limit, ct);
    }

    [Theory]
    [InlineData("INTERNAL")]
    [InlineData("PORTAL")]
    [InlineData("BOARD")]
    public async Task Invitation_recipient_live_delivers_canonical_transitions_and_stops_after_logout(string surface)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        using var recipient = app.CreateClient(); var cookie = await RegisterAndLogin(recipient);
        var profile = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        var actor = profile.GetProperty("id").GetGuid(); var email = profile.GetProperty("email").GetString()!;
        var invitations = app.Services.GetRequiredService<IInvitationService>();
        var boards = app.Services.GetRequiredService<BoardInvitationService>();
        async Task<InvitationOperation<CreatedInvitation>> Create() => surface == "BOARD"
            ? await boards.CreateAsync(f.Board, f.Owner, email, BoardRole.Member, "live-create", ct)
            : await invitations.CreateAsync(f.Organization, f.Owner, email,
                surface == "PORTAL" ? InvitationSurface.Portal : InvitationSurface.Internal,
                surface == "PORTAL" ? "OWNER" : "MEMBER", "live-create", ct);
        var before = await Create(); Assert.True(before.Succeeded);
        using var socket = await LiveSocket(app, cookie, "/invitations/live");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { null } });
        var initial = await StreamItem(socket);
        Assert.True(initial.GetProperty("resetRequired").GetBoolean()); Assert.Empty(initial.GetProperty("events").EnumerateArray());
        var created = await Create(); Assert.True(created.Succeeded);
        var changed = await StreamItem(socket);
        var creation = Assert.Single(changed.GetProperty("events").EnumerateArray());
        Assert.Equal("INVITATION_CREATED", creation.GetProperty("eventType").GetString());
        Assert.Equal("2", creation.GetProperty("sequence").GetString());
        Assert.Equal(new[] { "createdAt", "eventId", "eventType", "sequence" }, creation.EnumerateObject().Select(p => p.Name).Order());
        Assert.DoesNotContain(email, changed.GetRawText()); Assert.DoesNotContain(f.Organization.ToString(), changed.GetRawText());
        Assert.DoesNotContain(created.Value!.Invitation.Id.ToString(), changed.GetRawText());
        using var accepted = await Mutate(recipient, HttpMethod.Post,
            $"/me/invitations/{created.Value.Invitation.Id}/accept?expectedActorId={actor}", new { });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var acceptedPage = await StreamItem(socket);
        var transition = Assert.Single(acceptedPage.GetProperty("events").EnumerateArray());
        Assert.Equal("INVITATION_ACCEPTED", transition.GetProperty("eventType").GetString());
        Assert.Equal("3", transition.GetProperty("sequence").GetString());
        var stored = await app.Services.GetRequiredService<IInvitationStore>().FindByIdAsync(f.Organization, created.Value.Invitation.Id, ct);
        Assert.Equal(stored!.AcceptedAt, transition.GetProperty("createdAt").GetDateTimeOffset());
        if (surface == "PORTAL")
        {
            Assert.Null(await app.Services.GetRequiredService<IOrganizationStore>().FindMembershipAsync(f.Organization, actor, ct));
            Assert.Null(await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardMemberAsync(f.Board, actor, ct));
            Assert.True(await app.Services.GetRequiredService<IInvitationStore>().HasActivePortalAccessAsync(f.Organization, actor, ct));
        }
        using var logout = await Mutate(recipient, HttpMethod.Post, "/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while (true)
            {
                var frame = await Frame(socket, deadline.Token);
                if (frame is null || frame.Value.TryGetProperty("type", out var type) && type.GetInt32() is 3 or 7) break;
                Assert.NotEqual(2, frame.Value.GetProperty("type").GetInt32());
            }
        }
        catch (WebSocketException) { }
    }

    [Fact]
    public async Task Invitation_recipient_live_reconnect_replays_original_missed_revocation_once()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient(); var f = await NotificationFixture(app, owner, member, ct);
        using var recipient = app.CreateClient(); var cookie = await RegisterAndLogin(recipient);
        var profile = await recipient.GetFromJsonAsync<JsonElement>("/me", ct);
        var actor = profile.GetProperty("id").GetGuid(); var email = profile.GetProperty("email").GetString()!;
        var service = app.Services.GetRequiredService<IInvitationService>(); string cursor;
        InvitationOperation<CreatedInvitation> invite;
        using (var socket = await LiveSocket(app, cookie, "/invitations/live"))
        {
            await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { null } });
            await StreamItem(socket);
            invite = await service.CreateAsync(f.Organization, f.Owner, email, InvitationSurface.Internal, "MEMBER", "reconnect-create", ct);
            Assert.True(invite.Succeeded); var page = await StreamItem(socket);
            cursor = page.GetProperty("cursor").GetString()!;
        }
        Assert.True((await service.RevokeAsync(f.Organization, f.Owner, invite.Value!.Invitation.Id, "reconnect-revoke", ct)).Succeeded);
        var expected = await app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>().ReadAsync(actor, cursor, cancellationToken: ct);
        Assert.True(expected.Succeeded); var original = Assert.Single(expected.Value!.Events);
        using var reconnected = await LiveSocket(app, cookie, "/invitations/live");
        await SendFrame(reconnected, new { type = 4, invocationId = "watch", target = "Watch", arguments = new[] { cursor } });
        var replay = await StreamItem(reconnected); Assert.False(replay.GetProperty("resetRequired").GetBoolean());
        var actual = Assert.Single(replay.GetProperty("events").EnumerateArray());
        Assert.Equal(original.EventId, actual.GetProperty("eventId").GetGuid());
        Assert.Equal(original.CreatedAt, actual.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal("INVITATION_REVOKED", actual.GetProperty("eventType").GetString()); Assert.Equal("2", actual.GetProperty("sequence").GetString());
        await SendFrame(reconnected, new { type = 4, invocationId = "duplicate", target = "Watch", arguments = new[] { cursor } });
        var rejected = await Frame(reconnected, ct); Assert.Contains("subscription_limit", rejected!.Value.GetProperty("error").GetString());
        await SendFrame(reconnected, new { type = 5, invocationId = "watch" });
        var completed = await Frame(reconnected, ct); Assert.Equal(3, completed!.Value.GetProperty("type").GetInt32());
        await SendFrame(reconnected, new { type = 4, invocationId = "replacement", target = "Watch", arguments = new[] { replay.GetProperty("cursor").GetString() } });
        var empty = await StreamItem(reconnected); Assert.False(empty.GetProperty("resetRequired").GetBoolean()); Assert.Empty(empty.GetProperty("events").EnumerateArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://evil.example.test")]
    public async Task Invitation_recipient_live_rejects_untrusted_origin(string? origin)
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient(); await RegisterAndLogin(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/invitations/live/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1"); if (origin is not null) request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invitation_recipient_live_requires_authenticated_session()
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/invitations/live/negotiate?negotiateVersion=1");
        request.Headers.Add("X-StrataAI-Request", "1"); request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invitation_recipient_live_invalid_cursor_does_not_consume_subscription()
    {
        await using var app = new ApiFactory(); using var client = app.CreateClient(); var cookie = await RegisterAndLogin(client);
        using var socket = await LiveSocket(app, cookie, "/invitations/live");
        await SendFrame(socket, new { type = 4, invocationId = "invalid", target = "Watch", arguments = new[] { "" } });
        var rejected = await Frame(socket, TestContext.Current.CancellationToken);
        Assert.Contains("invalid_invitation_cursor", rejected!.Value.GetProperty("error").GetString());
        await SendFrame(socket, new { type = 4, invocationId = "replacement", target = "Watch", arguments = new[] { "tampered" } });
        var reset = await StreamItem(socket); Assert.True(reset.GetProperty("resetRequired").GetBoolean()); Assert.Empty(reset.GetProperty("events").EnumerateArray());
    }
}
