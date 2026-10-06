using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    // PRD-02/03-TC-05/07: account and assignment cleanup are one Demo transaction.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Demo_deactivation_restores_account_session_Card_assignments_and_events_after_cleanup_failure(bool keyed, bool cancel)
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        DeactivationCleanupFailureFixture? events = null;
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture());
            services.AddSingleton<IWorkEventStore>(provider => events = new DeactivationCleanupFailureFixture(
                (IWorkEventStore)provider.GetRequiredService<IWorkEventReader>(), cancellation));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var identities = app.Services.GetRequiredService<IIdentityStore>();
        var receipts = app.Services.GetRequiredService<IIdentityRevocationReplayStore>();
        var unit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var inner = app.Services.GetRequiredService<IdentityService>();
        var reader = app.Services.GetRequiredService<IWorkEventReader>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Deactivation rollback Card", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, card.Version, "fixture", ct)).Succeeded);
        var originalCard = await store.FindCardAsync(card.Id, ct);
        var originalUser = await identities.FindUserByIdAsync(f.Recipient, ct);
        var originalIdentityEvents = (await identities.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.ToArray();
        var originalWork = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var rawSession = f.RecipientCookie.Split('=', 2)[1];
        var sessionHash = app.Services.GetRequiredService<ISecureTokenService>().Hash(rawSession);
        var key = Guid.NewGuid();
        Task<IdentityOperation<bool>> Deactivate(CancellationToken token) => keyed
            ? unit.ExecuteRevocationAsync(f.Recipient, sessionHash, key, IdentityRevocationKind.Deactivate, "fixture",
                actor => inner.DeactivateAsync(actor, "fixture", token), token)
            : unit.ExecuteDeactivationAsync(f.Recipient, () => inner.DeactivateAsync(f.Recipient, "fixture", token), token, "fixture");
        events!.Armed = true; events.Cancel = cancel;
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Deactivate(cancellation.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Deactivate(ct));
        Assert.Equal(1, events.Failures);
        Assert.Equal(originalUser, await identities.FindUserByIdAsync(f.Recipient, ct));
        Assert.NotNull(await identities.FindActiveSessionAsync(sessionHash, DateTimeOffset.UtcNow, ct));
        Assert.Equal(originalIdentityEvents, (await identities.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.ToArray());
        Assert.Null(await receipts.ReadAsync(f.Recipient, key, ct));
        Assert.Equal(originalCard, await store.FindCardAsync(card.Id, ct));
        Assert.Contains((await work.ListCardMembersAsync(card.Id, f.Owner, cancellationToken: ct)).Value!.Items, row => row.UserId == f.Recipient);
        var restoredWork = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Equal(originalWork.Cursor, restoredWork.Cursor);
        Assert.Equal(originalWork.Events.Select(row => row.Event).ToArray(), restoredWork.Events.Select(row => row.Event).ToArray());
        using var authenticated = await recipient.GetAsync("/me", ct);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        events.Armed = false;
        Assert.True((await Deactivate(ct).WaitAsync(TimeSpan.FromSeconds(10), ct)).Succeeded);
        Assert.Equal(AccountStatus.Deactivated, (await identities.FindUserByIdAsync(f.Recipient, ct))!.Status);
        Assert.Null(await identities.FindActiveSessionAsync(sessionHash, DateTimeOffset.UtcNow, ct));
        Assert.Equal(originalCard!.Version + 1, (await store.FindCardAsync(card.Id, ct))!.Version);
        Assert.DoesNotContain((await work.ListCardMembersAsync(card.Id, f.Owner, cancellationToken: ct)).Value!.Items, row => row.UserId == f.Recipient);
        Assert.Equal(originalIdentityEvents.Length + 1, (await identities.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.Count);
        var committedWork = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Single(committedWork.Events.Skip(originalWork.Events.Count), row => row.Event.EventType == "CARD_MEMBER_REMOVED");
        if (keyed)
        {
            Assert.NotNull(await receipts.ReadAsync(f.Recipient, key, ct));
            Assert.True((await Deactivate(ct).WaitAsync(TimeSpan.FromSeconds(10), ct)).Succeeded);
            Assert.Equal(committedWork.Cursor, (await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct)).Cursor);
            Assert.Equal(originalIdentityEvents.Length + 1, (await identities.ReadEventsAsync(f.Recipient, 0, ct)).Value!.Events.Count);
        }
    }

    [Fact]
    public async Task Demo_deactivation_rollback_excludes_a_queued_Work_commit_and_releases_both_gates()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        DeactivationCleanupFailureFixture? events = null;
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture());
            services.AddSingleton<IWorkEventStore>(provider => events = new DeactivationCleanupFailureFixture(
                (IWorkEventStore)provider.GetRequiredService<IWorkEventReader>(), cancellation));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Cleanup gate Card", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await app.Services.GetRequiredService<IWorkManagementService>().SetCardMemberAsync(
            card.Id, f.Recipient, f.Owner, true, card.Version, "fixture", ct)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        events!.Armed = true;
        events.BeforeFailure = async () => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); };
        var inner = app.Services.GetRequiredService<IdentityService>();
        var deactivating = app.Services.GetRequiredService<IIdentityUnitOfWork>().ExecuteDeactivationAsync(f.Recipient,
            () => inner.DeactivateAsync(f.Recipient, "fixture", ct), ct, "fixture");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var retainedId = Guid.NewGuid();
        var queued = app.Services.GetRequiredService<IWorkManagementUnitOfWork>().ExecuteReadAsync(f.Organization, f.Owner,
            "fixture_refused", () => Task.FromResult(true), async () => {
                workEntered.SetResult();
                await store.CreateCardAsync(f.List, retainedId, "Committed after rollback", null, null, DateTimeOffset.UtcNow, ct);
                return WorkOperation<bool>.Success(true);
            }, ct);
        Assert.False(workEntered.Task.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => deactivating.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.True((await queued.WaitAsync(TimeSpan.FromSeconds(10), ct)).Succeeded);
        Assert.NotNull(await store.FindCardAsync(retainedId, ct));
        Assert.Equal(AccountStatus.Active, (await app.Services.GetRequiredService<IIdentityStore>().FindUserByIdAsync(f.Recipient, ct))!.Status);
        Assert.Contains((await app.Services.GetRequiredService<IWorkManagementService>().ListCardMembersAsync(
            card.Id, f.Owner, cancellationToken: ct)).Value!.Items, row => row.UserId == f.Recipient);
        events.Armed = false;
        Assert.True((await app.Services.GetRequiredService<IIdentityUnitOfWork>().ExecuteDeactivationAsync(f.Recipient,
            () => inner.DeactivateAsync(f.Recipient, "fixture", ct), ct, "fixture").WaitAsync(TimeSpan.FromSeconds(10), ct)).Succeeded);
        Assert.NotNull(await store.FindCardAsync(retainedId, ct));
    }

    private sealed class DeactivationCleanupFailureFixture(IWorkEventStore inner, CancellationTokenSource cancellation) : IWorkEventStore
    {
        public bool Armed { get; set; }
        public bool Cancel { get; set; }
        public Func<Task>? BeforeFailure { get; set; }
        public int Failures { get; private set; }
        public async Task AppendAsync(WorkEvent change, CancellationToken ct = default)
        {
            await inner.AppendAsync(change, ct);
            if (!Armed || change.EventType != "CARD_MEMBER_REMOVED") return;
            if (BeforeFailure is { } before) await before();
            Failures++;
            if (Cancel) cancellation.Cancel();
            else throw new InvalidOperationException("Deactivation cleanup failed after event publication.");
        }
    }
}
