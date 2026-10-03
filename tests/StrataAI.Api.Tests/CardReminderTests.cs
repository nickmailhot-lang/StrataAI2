using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task List_and_Board_archives_suspend_children_and_restore_only_active_contexts_without_enabling_cancelled_choices()
    {
        var ct = TestContext.Current.CancellationToken; var publisher = new ReminderPublisher();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICardReminderJobPublisher>(publisher));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>(); var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var otherList = await work.CreateListAsync(f.Board, Guid.NewGuid(), "Other context", null, DateTimeOffset.UtcNow, ct);
        async Task<CardRecord> Create(Guid list, string title)
        {
            var card = await work.CreateCardAsync(list, Guid.NewGuid(), title, null, null, DateTimeOffset.UtcNow, ct);
            using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
                new CardDatesInput(null, DateTimeOffset.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
            Assert.Equal(HttpStatusCode.OK, dates.StatusCode); return (await work.FindCardAsync(card.Id, ct))!;
        }
        var active = await Create(f.List, "Active child"); var archived = await Create(f.List, "Archived child");
        var cancelled = await Create(f.List, "Cancelled child"); var other = await Create(otherList.Id, "Other List child");
        foreach (var card in new[] { active, archived, cancelled, other })
        {
            using var set = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders", new CardReminderInput("1_HOUR", true, 2, 0));
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }
        using var cancel = await Mutate(recipient, HttpMethod.Delete, $"/cards/{cancelled.Id}/reminders?cardVersion=2&version=1", new { });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using var cardArchive = await Mutate(owner, HttpMethod.Post, $"/cards/{archived.Id}/archive", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, cardArchive.StatusCode);
        var original = (await reminders.FindAsync(f.Organization, f.Recipient, active.Id, ct))!;
        var cancelledRow = await reminders.FindAsync(f.Organization, f.Recipient, cancelled.Id, ct);
        var archivedRow = await reminders.FindAsync(f.Organization, f.Recipient, archived.Id, ct);
        var otherRow = await reminders.FindAsync(f.Organization, f.Recipient, other.Id, ct);
        var archiveKey = Guid.NewGuid().ToString();
        using var listArchive = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/archive", new { version = 1 }, archiveKey);
        Assert.Equal(HttpStatusCode.OK, listArchive.StatusCode);
        var suspended = (await reminders.FindAsync(f.Organization, f.Recipient, active.Id, ct))!;
        Assert.Equal(original.Id, suspended.Id); Assert.Equal(2, suspended.Generation); Assert.Equal("SUSPENDED", suspended.Status); Assert.Null(suspended.TriggerAt);
        Assert.Equal(otherRow, await reminders.FindAsync(f.Organization, f.Recipient, other.Id, ct));
        var board = (await work.FindBoardAsync(f.Board, ct))!;
        using var boardArchive = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/archive", new { version = board.Version });
        Assert.Equal(HttpStatusCode.OK, boardArchive.StatusCode);
        Assert.Equal("SUSPENDED", (await reminders.FindAsync(f.Organization, f.Recipient, other.Id, ct))!.Status);
        board = (await work.FindBoardAsync(f.Board, ct))!;
        using var boardRestore = await Mutate(owner, HttpMethod.Post, $"/boards/{f.Board}/restore", new { version = board.Version });
        Assert.Equal(HttpStatusCode.OK, boardRestore.StatusCode);
        Assert.Equal(suspended, await reminders.FindAsync(f.Organization, f.Recipient, active.Id, ct));
        var renewedOther = (await reminders.FindAsync(f.Organization, f.Recipient, other.Id, ct))!;
        Assert.Equal(3, renewedOther.Generation); Assert.Equal("SCHEDULED", renewedOther.Status);
        using var listRestore = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/restore", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, listRestore.StatusCode);
        var renewed = (await reminders.FindAsync(f.Organization, f.Recipient, active.Id, ct))!;
        Assert.Equal(original.Id, renewed.Id); Assert.Equal(3, renewed.Generation); Assert.Equal("SCHEDULED", renewed.Status);
        Assert.Equal(cancelledRow, await reminders.FindAsync(f.Organization, f.Recipient, cancelled.Id, ct));
        Assert.Equal(archivedRow, await reminders.FindAsync(f.Organization, f.Recipient, archived.Id, ct));
        Assert.Equal(6, publisher.Jobs.Count);
        using var replay = await Mutate(owner, HttpMethod.Post, $"/lists/{f.List}/archive", new { version = 1 }, archiveKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(6, publisher.Jobs.Count);
        Assert.Equal(renewed, await reminders.FindAsync(f.Organization, f.Recipient, active.Id, ct));
        Assert.Equal(2, (await work.FindCardAsync(active.Id, ct))!.Version);
    }

    [Fact]
    public async Task Card_archive_suspends_the_choice_and_restore_reschedules_without_reenabling_cancelled_choices()
    {
        var ct = TestContext.Current.CancellationToken; var publisher = new ReminderPublisher();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICardReminderJobPublisher>(publisher));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Archive reminder", null, null, DateTimeOffset.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, DateTimeOffset.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        var path = $"/cards/{card.Id}/reminders"; var input = new CardReminderInput("1_HOUR", true, 2, 0); var key = Guid.NewGuid().ToString();
        using var created = await Mutate(recipient, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var initial = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        using var archive = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var suspended = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        Assert.Equal(initial.Id, suspended.Id); Assert.Equal(2, suspended.Generation); Assert.Equal("SUSPENDED", suspended.Status);
        Assert.True(suspended.Enabled); Assert.Null(suspended.TriggerAt); Assert.Single(publisher.Jobs);
        using var hidden = await Mutate(recipient, HttpMethod.Post, path, input, key); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var restore = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/restore", new { version = 3 });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var scheduled = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        Assert.Equal(initial.Id, scheduled.Id); Assert.Equal(3, scheduled.Generation); Assert.Equal("SCHEDULED", scheduled.Status);
        Assert.Equal(2, publisher.Jobs.Count);
        using var cancel = await Mutate(recipient, HttpMethod.Delete, path + "?cardVersion=4&version=3", new { });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelled = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        using var archiveAgain = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 4 });
        Assert.Equal(HttpStatusCode.OK, archiveAgain.StatusCode);
        using var restoreAgain = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/restore", new { version = 5 });
        Assert.Equal(HttpStatusCode.OK, restoreAgain.StatusCode);
        Assert.Equal(cancelled, await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct)); Assert.Equal(2, publisher.Jobs.Count);
    }

    [Fact]
    public async Task Personal_reminders_validate_revisions_intervals_and_privacy_and_recover_set_cancel_receipts()
    {
        var ct = TestContext.Current.CancellationToken; var publisher = new ReminderPublisher();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICardReminderJobPublisher>(publisher));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Personal reminder", null, null, DateTimeOffset.UtcNow, ct);
        var path = $"/cards/{card.Id}/reminders";
        var initial = await recipient.GetFromJsonAsync<CardReminderState>(path, ct);
        Assert.NotNull(initial); Assert.Null(initial.Reminder); Assert.Empty(initial.Options); Assert.True(initial.CanChange);
        var input = new CardReminderInput("1_HOUR", true, 1, 0);
        using var absentDue = await Mutate(recipient, HttpMethod.Post, path, input);
        Assert.Equal(HttpStatusCode.BadRequest, absentDue.StatusCode); Assert.Empty(publisher.Jobs);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, DateTimeOffset.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode); input = input with { CardVersion = 2 };
        using var invalid = await Mutate(recipient, HttpMethod.Post, path, input with { IntervalCode = "FORGED" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var badVersion = await Mutate(recipient, HttpMethod.Post, path, input with { Version = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, badVersion.StatusCode);
        using var staleCard = await Mutate(recipient, HttpMethod.Post, path, input with { CardVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, staleCard.StatusCode);
        using var denied = await Mutate(outsider, HttpMethod.Post, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Empty(publisher.Jobs);
        var key = Guid.NewGuid().ToString();
        using var created = await Mutate(recipient, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var state = await created.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(state); Assert.NotNull(state.Reminder); Assert.True(state.Changed);
        Assert.Equal(f.Recipient, state.Reminder.UserId); Assert.Equal(1, state.Reminder.Version); Assert.Equal(1, state.Reminder.Generation);
        Assert.Equal("SCHEDULED", state.Reminder.Status); Assert.Equal(4, state.Options.Count); Assert.Single(publisher.Jobs);
        using var replay = await Mutate(recipient, HttpMethod.Post, path, input, key);
        Assert.Equal(await created.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var noop = await Mutate(recipient, HttpMethod.Post, path, input with { Version = 1 });
        var same = await noop.Content.ReadFromJsonAsync<CardReminderState>(ct); Assert.NotNull(same); Assert.False(same.Changed);
        Assert.Equal(state.Reminder, same.Reminder); Assert.Single(publisher.Jobs);
        var ownerState = await owner.GetFromJsonAsync<CardReminderState>(path + $"?userId={f.Recipient}", ct);
        Assert.NotNull(ownerState); Assert.Null(ownerState.Reminder); Assert.Equal(f.Owner, ownerState.UserId);
        using var stale = await Mutate(recipient, HttpMethod.Delete, path + "?cardVersion=2&version=0", new { });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var cancelKey = Guid.NewGuid().ToString();
        using var cancelled = await Mutate(recipient, HttpMethod.Delete, path + "?cardVersion=2&version=1", new { }, cancelKey);
        var off = await cancelled.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(off); Assert.NotNull(off.Reminder); Assert.True(off.Changed); Assert.False(off.Reminder.Enabled);
        Assert.Equal("CANCELLED", off.Reminder.Status); Assert.Null(off.Reminder.DueAt); Assert.Null(off.Reminder.TriggerAt);
        Assert.Equal(state.Reminder.Id, off.Reminder.Id); Assert.Equal(2, off.Reminder.Generation); Assert.Equal(2, off.Reminder.Version);
        using var cancelReplay = await Mutate(recipient, HttpMethod.Delete, path + "?cardVersion=2&version=1", new { }, cancelKey);
        Assert.Equal(await cancelled.Content.ReadAsStringAsync(ct), await cancelReplay.Content.ReadAsStringAsync(ct));
        using var restored = await Mutate(recipient, HttpMethod.Post, path, input with { Version = 2 });
        var on = await restored.Content.ReadFromJsonAsync<CardReminderState>(ct); Assert.NotNull(on); Assert.NotNull(on.Reminder);
        Assert.Equal(3, on.Reminder.Generation); Assert.Equal(state.Reminder.Id, on.Reminder.Id); Assert.Equal(2, publisher.Jobs.Count);
        Assert.Equal(2, (await work.FindCardAsync(card.Id, ct))!.Version);
        var events = await app.Services.GetRequiredService<IWorkEventReader>().ReadAsync(f.Organization, f.Board, 0, 100, ct);
        var privateEvents = events.Events.Where(e => e.Event.EntityType == "Reminder").ToArray();
        Assert.Equal(3, privateEvents.Length); Assert.All(privateEvents, e => Assert.False(e.EntityVisible));
        var ids = privateEvents.Select(e => e.Event.EventId).ToHashSet();
        var sync = await owner.GetFromJsonAsync<BoardSyncPage>($"/boards/{f.Board}/sync", ct); Assert.NotNull(sync);
        var hidden = sync.Events.Where(e => ids.Contains(e.EventId)).ToArray(); Assert.Equal(3, hidden.Length);
        Assert.All(hidden, e => { Assert.Equal("BOARD_INVALIDATED", e.EventType); Assert.Equal(f.Board, e.EntityId); Assert.Null(e.ActorId); Assert.Empty(e.Metadata); });
        using var remove = await Mutate(owner, HttpMethod.Delete, $"/boards/{f.Board}/members/{f.Recipient}", new { });
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var forbiddenReplay = await Mutate(recipient, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenReplay.StatusCode);
        using var forbiddenRead = await recipient.GetAsync(path, ct); Assert.Equal(HttpStatusCode.NotFound, forbiddenRead.StatusCode);
        Assert.Equal(2, publisher.Jobs.Count);
    }
}
