using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData(IdentityRevocationKind.Logout)]
    [InlineData(IdentityRevocationKind.Deactivate)]
    public async Task Revocation_expiry_after_real_receipt_publication_restores_session_account_assignments_and_events(IdentityRevocationKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new RevocationPublicationClock();
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture());
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IIdentityStore>();
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var cards = app.Services.GetRequiredService<IWorkManagementStore>();
        var events = app.Services.GetRequiredService<IWorkEventReader>();
        var receipts = app.Services.GetRequiredService<IIdentityRevocationReplayStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var inner = app.Services.GetRequiredService<IdentityService>();
        var rawSession = f.RecipientCookie.Split('=', 2)[1];
        var hash = app.Services.GetRequiredService<ISecureTokenService>().Hash(rawSession);
        var session = await store.FindRevocationSessionProofAsync(hash, ct);
        Assert.NotNull(session);
        var originalUser = await store.FindUserByIdAsync(f.Recipient, ct);
        var originalEvents = (await store.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.ToArray();
        var card = await cards.CreateCardAsync(f.List, Guid.NewGuid(), "Receipt expiry assignment", null, null, clock.Instant, ct);
        Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, card.Version, "fixture", ct)).Succeeded);
        var originalCard = await cards.FindCardAsync(card.Id, ct);
        var originalWork = await events.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var key = Guid.NewGuid(); var observed = false;
        clock.AfterReceipt = () => {
            var receipt = receipts.ReadAsync(f.Recipient, key, ct).GetAwaiter().GetResult();
            if (receipt is null) return null;
            Assert.Equal(session.SessionId, receipt.SessionId); Assert.Equal(kind, receipt.Kind);
            observed = true; return session.ExpiresAt;
        };
        Task<IdentityOperation<bool>> Revoke() => unit.ExecuteRevocationAsync(f.Recipient, hash, key, kind, "fixture",
            actor => kind == IdentityRevocationKind.Logout ? inner.LogoutAsync(rawSession, actor, "fixture", ct)
                : inner.DeactivateAsync(actor, "fixture", ct), ct);
        var denied = await Revoke();
        Assert.True(observed); Assert.False(denied.Succeeded); Assert.Equal("session_unavailable", denied.ErrorCode);
        clock.AfterReceipt = null;
        Assert.Equal(originalUser, await store.FindUserByIdAsync(f.Recipient, ct));
        Assert.Equal(session, await store.FindRevocationSessionProofAsync(hash, ct));
        Assert.NotNull(await store.FindActiveSessionAsync(hash, clock.Instant, ct));
        Assert.Null(await receipts.ReadAsync(f.Recipient, key, ct));
        Assert.Equal(originalEvents, (await store.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.ToArray());
        Assert.Equal(originalCard, await cards.FindCardAsync(card.Id, ct));
        Assert.Contains((await work.ListCardMembersAsync(card.Id, f.Owner, cancellationToken: ct)).Value!.Items, row => row.UserId == f.Recipient);
        var restoredWork = await events.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Equal(originalWork.Cursor, restoredWork.Cursor);
        Assert.Equal(originalWork.Events.Select(row => row.Event).ToArray(), restoredWork.Events.Select(row => row.Event).ToArray());
        Assert.True((await Revoke().WaitAsync(TimeSpan.FromSeconds(10), ct)).Succeeded);
        Assert.True((await Revoke()).Succeeded);
        Assert.NotNull(await receipts.ReadAsync(f.Recipient, key, ct));
        Assert.Equal(originalEvents.Length + 1, (await store.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.Count);
        Assert.Null(await store.FindActiveSessionAsync(hash, clock.Instant, ct));
    }

    private sealed class RevocationPublicationClock : IClock
    {
        private bool _probing;
        public DateTimeOffset Instant { get; } = DateTimeOffset.UtcNow;
        public Func<DateTimeOffset?>? AfterReceipt { get; set; }
        public DateTimeOffset UtcNow
        {
            get
            {
                // The real Demo receipt reader checks this clock synchronously.
                // Its nested read must see the original instant during observation.
                if (_probing) return Instant;
                try { _probing = true; return AfterReceipt?.Invoke() ?? Instant; }
                finally { _probing = false; }
            }
        }
    }
}
