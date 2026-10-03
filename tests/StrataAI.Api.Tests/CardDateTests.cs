using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task Date_commands_reschedule_personal_reminders_once_per_generation_and_suspend_on_completion_or_clear()
    {
        var ct = TestContext.Current.CancellationToken; var publisher = new ReminderPublisher();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<ICardReminderJobPublisher>(publisher));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Scheduled dates", null, null, DateTimeOffset.UtcNow, ct);
        var due = DateTimeOffset.UtcNow.AddDays(2); var path = $"/cards/{card.Id}/dates";
        var input = new CardDatesInput(null, due.ToString("O"), "UTC", true, false, 1);
        using var initial = await Mutate(owner, HttpMethod.Patch, path, input);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        card = (await work.FindCardAsync(card.Id, ct))!;
        var personal = await reminders.SetAsync(card, f.Recipient, "1_HOUR", true, 0, DateTimeOffset.UtcNow, ct);
        var disabled = await reminders.SetAsync(card, f.Owner, "AT_DUE", false, 0, DateTimeOffset.UtcNow, ct);
        Assert.NotNull(personal); Assert.NotNull(disabled); Assert.Empty(publisher.Jobs);
        input = input with { DueAt = due.AddDays(1).ToString("O"), Version = 2 };
        var key = Guid.NewGuid().ToString();
        using var changed = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var scheduled = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        Assert.Equal(personal.Id, scheduled.Id); Assert.Equal(2, scheduled.Generation); Assert.Equal("SCHEDULED", scheduled.Status);
        Assert.Equal(scheduled.DueAt!.Value.AddHours(-1), scheduled.TriggerAt);
        var job = Assert.Single(publisher.Jobs);
        Assert.Equal(new CardReminderAttempt(scheduled.Id, scheduled.Generation), CardReminderAttempt.Parse(job.SafeMetadataJson));
        Assert.Equal(scheduled.TriggerAt, job.AvailableAt); Assert.Equal(f.Owner, job.ActorId);
        using var replay = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(await changed.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var noop = await Mutate(owner, HttpMethod.Patch, path, input with { Version = 3 });
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode); Assert.Single(publisher.Jobs);
        Assert.Equal(scheduled, await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct));
        using var completed = await Mutate(owner, HttpMethod.Patch, path, input with { DueComplete = true, Version = 3 });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var suspended = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        Assert.Equal(3, suspended.Generation); Assert.True(suspended.Enabled); Assert.Equal("SUSPENDED", suspended.Status);
        Assert.Null(suspended.TriggerAt); Assert.Single(publisher.Jobs);
        using var reopened = await Mutate(owner, HttpMethod.Patch, path, input with { Version = 4 });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode); Assert.Equal(2, publisher.Jobs.Count);
        Assert.NotEqual(publisher.Jobs[0].IdempotencyKey, publisher.Jobs[1].IdempotencyKey);
        Assert.Equal(4, (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!.Generation);
        using var clear = await Mutate(owner, HttpMethod.Patch, path, new CardDatesInput(null, null, null, false, false, 5));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = (await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        Assert.Equal(5, cleared.Generation); Assert.Null(cleared.DueAt); Assert.Null(cleared.TriggerAt);
        Assert.Equal("SUSPENDED", cleared.Status); Assert.Equal(2, publisher.Jobs.Count);
        Assert.Equal(disabled, await reminders.FindAsync(f.Organization, f.Owner, card.Id, ct));
    }

    private sealed class ReminderPublisher : ICardReminderJobPublisher
    {
        public List<StrataAI.Application.BackgroundJobs.NewBackgroundJob> Jobs { get; } = [];
        public Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
        { Jobs.Add(CardReminderJobs.Create(reminder, actorId, correlationId)); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Card_dates_roundtrip_replay_noop_completion_clear_and_current_scope_admission()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter<WorkItemLifecycleState>(JsonNamingPolicy.CamelCase));
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var recipient = app.CreateClient(); using var outsider = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct); await RegisterAndLogin(outsider);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Dates", null, null, DateTimeOffset.UtcNow, ct);
        using var watch = await Mutate(recipient, HttpMethod.Put, $"/watch/CARD/{card.Id}?version=0", new { });
        Assert.Equal(HttpStatusCode.OK, watch.StatusCode);
        var path = $"/cards/{card.Id}/dates"; var key = Guid.NewGuid().ToString();
        var input = new CardDatesInput("2026-03-08", "2026-03-08", "America/Vancouver", false, false, 1);
        using var denied = await Mutate(outsider, HttpMethod.Patch, path, input);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var invalid = await Mutate(owner, HttpMethod.Patch, path, input with { DueTimezone = "Unknown/Place" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(1, (await work.FindCardAsync(card.Id, ct))!.Version);
        using var changed = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var result = await changed.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(result); Assert.True(result.Changed);
        Assert.Equal(2, result.Card.Version); Assert.False(result.Card.DueHasTime); Assert.NotNull(result.Card.DueAt);
        Assert.Equal("America/Vancouver", result.Card.DueTimezone);
        using var replay = await Mutate(owner, HttpMethod.Patch, path, input, key);
        Assert.Equal(await changed.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        using var stale = await Mutate(owner, HttpMethod.Patch, path, input); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var noop = await Mutate(owner, HttpMethod.Patch, path, input with { Version = 2 });
        var same = await noop.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(same); Assert.False(same.Changed); Assert.Equal(2, same.Card.Version);
        Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        var complete = new CardDatesInput(result.Card.StartAt!.Value.ToString("O"), result.Card.DueAt!.Value.ToString("O"), "America/Vancouver", false, true, 2);
        using var completed = await Mutate(owner, HttpMethod.Patch, path, complete);
        var done = await completed.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(done); Assert.True(done.Card.DueComplete);
        Assert.Equal(WorkItemLifecycleState.Active, done.Card.LifecycleState); Assert.Equal(3, done.Card.Version);
        using var reopened = await Mutate(owner, HttpMethod.Patch, path, complete with { DueComplete = false, Version = 3 });
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        using var clear = await Mutate(owner, HttpMethod.Patch, path, new CardDatesInput(null, null, null, false, false, 4));
        var cleared = await clear.Content.ReadFromJsonAsync<CardDateChange>(json, ct); Assert.NotNull(cleared); Assert.Null(cleared.Card.DueAt);
        Assert.Null(cleared.Card.StartAt); Assert.Null(cleared.Card.DueTimezone); Assert.False(cleared.Card.DueComplete); Assert.Equal(5, cleared.Card.Version);
        var inbox = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        Assert.Equal(4, inbox.Count); Assert.Equal(2, inbox.Count(n => n.NotificationType == "CARD_DATE_CHANGED"));
        Assert.Single(inbox, n => n.NotificationType == "CARD_DUE_COMPLETED"); Assert.Single(inbox, n => n.NotificationType == "CARD_DUE_REOPENED");
        using var archive = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 5 });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var hiddenReplay = await Mutate(owner, HttpMethod.Patch, path, input, key); Assert.Equal(HttpStatusCode.NotFound, hiddenReplay.StatusCode);
        using var archiveChange = await Mutate(owner, HttpMethod.Patch, path, input with { Version = 6 }); Assert.Equal(HttpStatusCode.NotFound, archiveChange.StatusCode);
    }
}
