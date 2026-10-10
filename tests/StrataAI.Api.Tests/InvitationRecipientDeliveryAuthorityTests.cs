using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("authority")]
    [InlineData("account")]
    public async Task Invitation_recipient_delivery_boundary_recovers_only_actual_authority_change(string change)
    {
        var ct = TestContext.Current.CancellationToken; RecipientDeliveryBoundary? boundary = null;
        await using var app = new ApiFactory(configureServices: services =>
        {
            var original = services.Last(s => s.ServiceType == typeof(IIdentityUnitOfWork));
            services.AddSingleton<IIdentityUnitOfWork>(p => boundary = new RecipientDeliveryBoundary(
                (IIdentityUnitOfWork)ActivatorUtilities.CreateInstance(p, original.ImplementationType!)));
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var profile = await member.GetFromJsonAsync<JsonElement>("/me", ct);
        var email = profile.GetProperty("email").GetString()!;
        using var createdBoard = await Mutate(owner, HttpMethod.Post, "/boards", new
        { organizationId = f.Organization, name = "Delivery boundary Board", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, createdBoard.StatusCode);
        var board = (await createdBoard.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var invite = await app.Services.GetRequiredService<BoardInvitationService>().CreateAsync(
            board, f.Owner, email, BoardRole.Member, "delivery-boundary-create", ct);
        Assert.True(invite.Succeeded);
        using var socket = await LiveSocket(app, f.RecipientCookie, $"/invitations/live?expectedActorId={f.Recipient}");
        await SendFrame(socket, new { type = 4, invocationId = "watch", target = "Watch", arguments = new string?[] { null } });
        var initial = await StreamItem(socket);
        Assert.True(initial.GetProperty("resetRequired").GetBoolean()); Assert.Empty(initial.GetProperty("events").EnumerateArray());
        Assert.NotNull(boundary); boundary.Actor = f.Recipient;
        boundary.AfterAcceptedRead = async () =>
        {
            using var changed = change == "authority"
                ? await Mutate(owner, HttpMethod.Patch, $"/boards/{board}", new { name = "Changed delivery Board", version = 1 })
                : await Mutate(member, HttpMethod.Patch, "/me", new { displayName = "Changed delivery profile", version = profile.GetProperty("version").GetInt64() });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        };
        using var accepted = await Mutate(member, HttpMethod.Post, $"/me/invitations/{invite.Value!.Invitation.Id}/accept", new { });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        if (change == "account")
        {
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
            Assert.True(boundary.Changed); return;
        }
        var reset = await StreamItem(socket);
        Assert.True(reset.GetProperty("resetRequired").GetBoolean()); Assert.Empty(reset.GetProperty("events").EnumerateArray());
        var recovered = await StreamItem(socket);
        Assert.False(recovered.GetProperty("resetRequired").GetBoolean());
        var source = Assert.Single(recovered.GetProperty("events").EnumerateArray());
        Assert.Equal("INVITATION_ACCEPTED", source.GetProperty("eventType").GetString());
        Assert.Equal(boundary.SelectedEventId, source.GetProperty("eventId").GetGuid());
        Assert.Equal(new[] { "createdAt", "eventId", "eventType", "sequence" }, source.EnumerateObject().Select(p => p.Name).Order());
        Assert.DoesNotContain(email, recovered.GetRawText()); Assert.DoesNotContain(board.ToString(), recovered.GetRawText());
        Assert.DoesNotContain(f.Organization.ToString(), recovered.GetRawText()); Assert.True(boundary.Changed);
        var replay = app.Services.GetRequiredService<TransactionalInvitationRecipientSynchronization>();
        Assert.False((await replay.IsCursorCurrentAsync(f.Recipient, boundary.SelectedCursor!, ct)).Value);
        var next = await replay.ReadAsync(f.Recipient, recovered.GetProperty("cursor").GetString(), cancellationToken: ct);
        Assert.True(next.Succeeded); Assert.Empty(next.Value!.Events);
    }

    // Execute the actual HTTP change after the owning observation transaction
    // releases its locks, but before the hub's final session/cursor admission.
    private sealed class RecipientDeliveryBoundary(IIdentityUnitOfWork inner) : IIdentityUnitOfWork
    {
        private int changed;
        public Guid Actor { get; set; }
        public Func<Task>? AfterAcceptedRead { get; set; }
        public bool Changed { get; private set; }
        public Guid SelectedEventId { get; private set; }
        public string? SelectedCursor { get; private set; }
        public async Task<IdentityOperation<T>> ExecuteObservationAsync<T>(Guid actorId, Guid? organizationId,
            Func<Task<IdentityOperation<T>>> operation, CancellationToken cancellationToken = default)
        {
            var result = await inner.ExecuteObservationAsync(actorId, organizationId, operation, cancellationToken);
            if (actorId == Actor && result.Succeeded && result.Value is InvitationRecipientSyncPage page
                && page.Events.FirstOrDefault(e => e.EventType == "INVITATION_ACCEPTED") is { } accepted
                && AfterAcceptedRead is { } change && Interlocked.CompareExchange(ref changed, 1, 0) == 0)
            {
                SelectedEventId = accepted.EventId; SelectedCursor = page.Cursor;
                await change(); Changed = true;
            }
            return result;
        }
        public Task<IdentityOperation<T>> ExecuteAsync<T>(Guid actorId, Func<Task<IdentityOperation<T>>> operation, CancellationToken ct = default)
            => inner.ExecuteAsync(actorId, operation, ct);
        public Task<IdentityOperation<bool>> ExecuteDeactivationAsync(Guid actorId, Func<Task<IdentityOperation<bool>>> operation, CancellationToken ct = default, string correlationId = "")
            => inner.ExecuteDeactivationAsync(actorId, operation, ct, correlationId);
        public Task<IdentityOperation<bool>> ExecuteRevocationAsync(Guid actorId, string sessionHash, Guid key, IdentityRevocationKind kind, string correlationId, Func<Guid, Task<IdentityOperation<bool>>> operation, CancellationToken ct = default)
            => inner.ExecuteRevocationAsync(actorId, sessionHash, key, kind, correlationId, operation, ct);
        public Task<T> ExecuteRecoveryRequestAsync<T>(Func<Task<T>> operation, T neutralResult, CancellationToken ct = default)
            => inner.ExecuteRecoveryRequestAsync(operation, neutralResult, ct);
        public Task<IdentityOperation<UserProfile>> ExecuteTokenProofAsync(Func<Task<IdentityOperation<UserProfile>>> operation, CancellationToken ct = default)
            => inner.ExecuteTokenProofAsync(operation, ct);
        public Task<IdentityOperation<RegistrationOutcome>> ExecuteRegistrationAsync(Func<Task<IdentityOperation<RegistrationOutcome>>> operation, CancellationToken ct = default)
            => inner.ExecuteRegistrationAsync(operation, ct);
        public Task<IdentityOperation<LoginOutcome>> ExecuteSignInAsync(Func<Task<IdentityOperation<LoginOutcome>>> operation, CancellationToken ct = default)
            => inner.ExecuteSignInAsync(operation, ct);
    }
}
