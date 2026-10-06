using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("actor")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task PRD_03_Demo_Organization_command_restores_cross_store_mutations_unless_committed(string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var actorFence = new OrganizationTransactionActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(actorFence));
        using var client = app.CreateClient();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var unit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var org = Guid.NewGuid(); var actor = Guid.NewGuid(); var board = Guid.NewGuid();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<OrganizationOperation<bool>> Mutate()
        {
            await organizations.CreateOrganizationAsync(actor, org, "Rollback private Organization", null, DateTimeOffset.UtcNow, ct);
            await work.CreateBoardAsync(org, actor, board, "Rollback private Board", null, BoardVisibility.Private, "COLOR", "blue", DateTimeOffset.UtcNow, ct);
            if (outcome == "actor") actorFence.Allowed = false;
            if (outcome == "exception") throw new InvalidOperationException("fixture failure after mutation");
            if (outcome == "cancel") cancel.Cancel();
            return outcome == "failure" ? OrganizationOperation<bool>.Failure("fixture_refused") : OrganizationOperation<bool>.Success(true);
        }
        if (outcome == "exception")
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(org, actor, null, true, Mutate, cancel.Token));
        else if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(org, actor, null, true, Mutate, cancel.Token));
        else
        {
            var result = await unit.ExecuteAsync(org, actor, null, true, Mutate, cancel.Token);
            Assert.Equal(outcome == "success", result.Succeeded);
            if (outcome == "actor") Assert.Equal("session_unavailable", result.ErrorCode);
        }
        Assert.Equal(outcome == "success", await organizations.FindOrganizationAsync(org, ct) is not null);
        Assert.Equal(outcome == "success", await organizations.FindMembershipAsync(org, actor, ct) is not null);
        Assert.Equal(outcome == "success", await work.FindBoardAsync(board, ct) is not null);
        // Refusal must release both gates, allowing a fresh command to commit.
        actorFence.Allowed = true;
        var fresh = Guid.NewGuid();
        Assert.True((await unit.ExecuteAsync(fresh, actor, null, true, async () => {
            await organizations.CreateOrganizationAsync(actor, fresh, "Fresh after rollback", null, DateTimeOffset.UtcNow, ct);
            return OrganizationOperation<bool>.Success(true);
        }, ct)).Succeeded);
    }

    [Fact]
    public async Task PRD_03_Demo_Organization_rollback_does_not_overwrite_a_queued_Work_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture()));
        using var client = app.CreateClient();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var orgUnit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var workUnit = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var org = Guid.NewGuid(); var actor = Guid.NewGuid(); var board = Guid.NewGuid();
        await organizations.CreateOrganizationAsync(actor, org, "Concurrent Organization", null, DateTimeOffset.UtcNow, ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var denied = orgUnit.ExecuteAsync(org, actor, null, false, async () => {
            await organizations.UpdateOrganizationAsync(org, "Uncommitted name", null, null, 1, DateTimeOffset.UtcNow, ct);
            entered.SetResult(); await release.Task.WaitAsync(ct);
            return OrganizationOperation<bool>.Failure("fixture_refused");
        }, ct);
        await entered.Task.WaitAsync(ct);
        var later = workUnit.ExecuteReadAsync(org, actor, "fixture_refused", () => Task.FromResult(true), async () => {
            dispatched.SetResult();
            await work.CreateBoardAsync(org, actor, board, "Retained Work commit", null, BoardVisibility.Private, "COLOR", "blue", DateTimeOffset.UtcNow, ct);
            return WorkOperation<bool>.Success(true);
        }, ct);
        // WaitAsync on the occupied gate has synchronously yielded; Work must
        // not capture or mutate stores until Organization restores its snapshot.
        Assert.False(dispatched.Task.IsCompleted);
        release.SetResult();
        Assert.False((await denied.WaitAsync(ct)).Succeeded);
        Assert.True((await later.WaitAsync(ct)).Succeeded);
        Assert.Equal("Concurrent Organization", (await organizations.FindOrganizationAsync(org, ct))!.Name);
        Assert.NotNull(await work.FindBoardAsync(board, ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PRD_03_Demo_member_removal_or_departure_restores_membership_assignment_Card_revision_and_events_after_final_actor_loss(bool departing)
    {
        var ct = TestContext.Current.CancellationToken;
        var fence = new OrganizationTransactionActorFixture();
        OrganizationEventActorLossFixture? events = null;
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<ICommandActorAuthorization>(fence);
            services.AddSingleton<IWorkEventStore>(provider => events = new OrganizationEventActorLossFixture(
                (IWorkEventStore)provider.GetRequiredService<IWorkEventReader>(), fence));
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementService>();
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var orgService = app.Services.GetRequiredService<IOrganizationService>();
        var reader = app.Services.GetRequiredService<IWorkEventReader>();
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Assigned rollback Card", null, null, DateTimeOffset.UtcNow, ct);
        Assert.True((await work.SetCardMemberAsync(card.Id, f.Recipient, f.Owner, true, card.Version, "fixture", ct)).Succeeded);
        var originalCard = (await store.FindCardAsync(card.Id, ct))!;
        var originalMembership = await organizations.FindMembershipAsync(f.Organization, f.Recipient, ct);
        var before = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var key = Guid.NewGuid();
        var receipts = app.Services.GetRequiredService<IOrganizationDepartureReplayStore>();
        events!.Armed = true;
        var refused = departing
            ? await orgService.LeaveAsync(f.Organization, f.Recipient, "fixture", ct, key)
            : await orgService.RemoveMemberAsync(f.Organization, f.Owner, f.Recipient, "fixture", ct);
        Assert.False(refused.Succeeded); Assert.Equal("session_unavailable", refused.ErrorCode);
        Assert.Equal(1, events.Withdrawals);
        Assert.Null(await receipts.ReadAsync(f.Organization, f.Recipient, key, ct));
        Assert.Equal(originalCard, await store.FindCardAsync(card.Id, ct));
        Assert.Equal(originalMembership, await organizations.FindMembershipAsync(f.Organization, f.Recipient, ct));
        var after = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Equal(before.Cursor, after.Cursor);
        Assert.Equal(before.Events.Select(row => row.Event).ToArray(), after.Events.Select(row => row.Event).ToArray());
        fence.Allowed = true;
        var assignments = await work.ListCardMembersAsync(card.Id, f.Owner, cancellationToken: ct);
        Assert.True(assignments.Succeeded);
        Assert.Contains(assignments.Value!.Items, row => row.UserId == f.Recipient);
        events.Armed = false;
        var removed = departing
            ? await orgService.LeaveAsync(f.Organization, f.Recipient, "fixture", ct, key)
            : await orgService.RemoveMemberAsync(f.Organization, f.Owner, f.Recipient, "fixture", ct);
        Assert.True(removed.Succeeded);
        if (departing) Assert.NotNull(await receipts.ReadAsync(f.Organization, f.Recipient, key, ct));
        Assert.False((await organizations.FindMembershipAsync(f.Organization, f.Recipient, ct))!.Active);
        Assert.Equal(originalCard.Version + 1, (await store.FindCardAsync(card.Id, ct))!.Version);
        Assert.DoesNotContain((await work.ListCardMembersAsync(card.Id, f.Owner, cancellationToken: ct)).Value!.Items,
            row => row.UserId == f.Recipient);
    }

    [Fact]
    public async Task PRD_03_Demo_deletion_restores_Organization_reminder_and_event_after_final_actor_loss()
    {
        var ct = TestContext.Current.CancellationToken;
        var fence = new OrganizationTransactionActorFixture();
        OrganizationEventActorLossFixture? events = null;
        await using var app = new ApiFactory(configureServices: services => {
            services.AddSingleton<ICommandActorAuthorization>(fence);
            services.AddSingleton<IWorkEventStore>(provider => events = new OrganizationEventActorLossFixture(
                (IWorkEventStore)provider.GetRequiredService<IWorkEventReader>(), fence));
        });
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var store = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var orgService = app.Services.GetRequiredService<IOrganizationService>();
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var dates = app.Services.GetRequiredService<ICardDateStore>();
        var reader = app.Services.GetRequiredService<IWorkEventReader>();
        var now = DateTimeOffset.UtcNow;
        var card = await store.CreateCardAsync(f.List, Guid.NewGuid(), "Reminder rollback Card", null, null, now, ct);
        card = (await dates.SetDatesAsync(f.Organization, f.Board, card.Id, new(null, now.AddDays(2), "UTC", true, false), 1, now, ct))!;
        var originalReminder = (await reminders.SetAsync(card, f.Owner, "AT_DUE", true, 0, now, ct))!;
        var originalOrganization = (await organizations.FindOrganizationAsync(f.Organization, ct))!;
        var before = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        events!.EventType = "REMINDER_CANCELLED"; events.Armed = true;
        var refused = await orgService.MarkDeletingAsync(f.Organization, f.Owner, originalOrganization.Version, "fixture", ct);
        Assert.False(refused.Succeeded); Assert.Equal("session_unavailable", refused.ErrorCode);
        Assert.Equal(1, events.Withdrawals);
        Assert.Equal(originalOrganization, await organizations.FindOrganizationAsync(f.Organization, ct));
        Assert.Equal(originalReminder, await reminders.FindAsync(f.Organization, f.Owner, card.Id, ct));
        Assert.Equal(card, await store.FindCardAsync(card.Id, ct));
        var after = await reader.ReadAsync(f.Organization, f.Board, 0, 100, ct);
        Assert.Equal(before.Cursor, after.Cursor);
        Assert.Equal(before.Events.Select(row => row.Event).ToArray(), after.Events.Select(row => row.Event).ToArray());
        fence.Allowed = true; events.Armed = false;
        Assert.True((await orgService.MarkDeletingAsync(f.Organization, f.Owner, originalOrganization.Version, "fixture", ct)).Succeeded);
        Assert.Equal(OrganizationStatus.Deleting, (await organizations.FindOrganizationAsync(f.Organization, ct))!.Status);
        var suspended = (await reminders.FindAsync(f.Organization, f.Owner, card.Id, ct))!;
        Assert.Equal("SUSPENDED", suspended.Status); Assert.Null(suspended.TriggerAt);
        Assert.Equal(originalReminder.Generation + 1, suspended.Generation);
        Assert.Equal(originalReminder.Version + 1, suspended.Version);
        Assert.Equal(card, await store.FindCardAsync(card.Id, ct));
    }

    [Fact]
    public async Task PRD_03_Demo_navigation_waits_for_Organization_rollback_then_acquires_its_Work_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = new ApiFactory(configureServices: services =>
            services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture()));
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var orgUnit = app.Services.GetRequiredService<IOrganizationUnitOfWork>();
        var identityUnit = app.Services.GetRequiredService<IIdentityUnitOfWork>();
        var navigation = app.Services.GetRequiredService<INavigationInteractionEventStore>();
        var board = (await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardAsync(f.Board, ct))!;
        var originalOrganization = (await organizations.FindOrganizationAsync(f.Organization, ct))!;
        var source = NavigationInteractionEvent.BoardOpened(Guid.NewGuid(), f.Owner, f.Organization, f.Board, board.Version, DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changing = orgUnit.ExecuteAsync(f.Organization, f.Owner, null, false, async () => {
            await organizations.UpdateOrganizationAsync(f.Organization, "Uncommitted navigation scope", null, null,
                originalOrganization.Version, DateTimeOffset.UtcNow, ct);
            entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return OrganizationOperation<bool>.Failure("fixture_refused");
        }, ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var opening = identityUnit.ExecuteAsync(f.Owner, async () => {
            navigationEntered.SetResult();
            return IdentityOperation<bool>.Success(await navigation.AppendAuthorizedAsync(source, ct));
        }, ct);
        Assert.False(navigationEntered.Task.IsCompleted);
        release.SetResult();
        Assert.False((await changing.WaitAsync(TimeSpan.FromSeconds(10), ct)).Succeeded);
        var opened = await opening.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(opened.Succeeded); Assert.True(opened.Value);
        Assert.Equal(originalOrganization, await organizations.FindOrganizationAsync(f.Organization, ct));
        Assert.Equal(board, await app.Services.GetRequiredService<IWorkManagementStore>().FindBoardAsync(f.Board, ct));
        // The immutable original can still be replayed through the same two gates.
        var replay = await identityUnit.ExecuteAsync(f.Owner, async () =>
            IdentityOperation<bool>.Success(await navigation.AppendAuthorizedAsync(source, ct)), ct);
        Assert.True(replay.Succeeded); Assert.True(replay.Value);
    }

    private sealed class OrganizationEventActorLossFixture(IWorkEventStore inner,
        OrganizationTransactionActorFixture fence) : IWorkEventStore
    {
        public bool Armed { get; set; }
        public string EventType { get; set; } = "CARD_MEMBER_REMOVED";
        public int Withdrawals { get; private set; }
        public async Task AppendAsync(WorkEvent change, CancellationToken cancellationToken = default)
        {
            await inner.AppendAsync(change, cancellationToken);
            if (Armed && change.EventType == EventType)
            { Withdrawals++; fence.Allowed = false; }
        }
    }

    private sealed class OrganizationTransactionActorFixture : ICommandActorAuthorization
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Allowed); }
    }
}
