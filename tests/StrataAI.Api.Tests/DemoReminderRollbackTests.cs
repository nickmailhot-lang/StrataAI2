using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class DemoDeliveryAfterAppend(IWorkEventStore inner, Action<WorkEvent> after) : IWorkEventStore
    {
        public async Task AppendAsync(WorkEvent change, CancellationToken ct = default)
        {
            await inner.AppendAsync(change, ct);
            if (change.EventType == "REMINDER_FIRED") after(change);
        }
    }

    [Theory]
    [InlineData("lease")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task ARCH_03_Demo_reminder_rolls_back_actual_source_and_all_effects_before_retry(string mode)
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ClaimedBackgroundJob? claim = null; var armed = true; var sources = new List<WorkEvent>();
        var failure = new InvalidOperationException("Fixture failure after actual reminder source.");
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IWorkEventStore>(p => new DemoDeliveryAfterAppend(p.GetRequiredService<InMemoryWorkEventStore>(), source =>
            {
                sources.Add(source);
                if (!armed) return;
                armed = false;
                if (mode == "lease") clock.UtcNow = claim!.LeaseExpiresAt;
                if (mode == "exception") throw failure;
                if (mode == "cancel") cancel.Cancel();
            }));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Demo atomic reminder", null, null, clock.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        using var response = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders",
            new CardReminderInput("1_HOUR", true, 2, 0), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(state); Assert.NotNull(state.Reminder);
        clock.UtcNow = state.Reminder.TriggerAt!.Value;
        var queue = app.Services.GetRequiredService<IBackgroundJobStore>();
        claim = await queue.ClaimAsync(f.Organization, Guid.NewGuid(), ct); Assert.NotNull(claim);
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var journal = app.Services.GetRequiredService<INotificationRealtimeStore>();
        var concrete = Assert.IsType<InMemoryWorkManagementStore>(work);
        var original = await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct);
        var originalNotifications = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        var originalSequence = await journal.GetRecipientSequenceAsync(f.Organization, f.Recipient, ct);
        var originalAudits = concrete.AuditSnapshot(f.Organization);
        var delivery = app.Services.GetRequiredService<ICardReminderDeliveryStore>();
        Task<CardReminderDeliveryResult> Attempt() => delivery.DeliverAsync(claim, CardReminderAttempt.Parse(claim.SafeMetadataJson), cancel.Token);
        if (mode == "lease") Assert.Equal(CardReminderDeliveryResult.LeaseLost, await Attempt());
        else if (mode == "exception") Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(Attempt));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(Attempt);
        var tentative = Assert.Single(sources);
        Assert.Equal(claim.Id, tentative.EventId);
        Assert.False(app.Services.GetRequiredService<InMemoryWorkEventStore>().ContainsExact(tentative));
        Assert.Equal(original, await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct));
        Assert.Equal(originalNotifications, await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        Assert.Equal(originalSequence, await journal.GetRecipientSequenceAsync(f.Organization, f.Recipient, ct));
        Assert.Equal(originalAudits, concrete.AuditSnapshot(f.Organization));
        var retry = mode == "lease" ? await queue.ClaimAsync(f.Organization, Guid.NewGuid(), ct) : claim;
        Assert.NotNull(retry); Assert.Equal(claim.Id, retry.Id);
        await new CardReminderDeliveryHandler(delivery).ExecuteAsync(retry, ct);
        Assert.Equal(2, sources.Count);
        Assert.True(app.Services.GetRequiredService<InMemoryWorkEventStore>().ContainsExact(sources[1]));
        Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct),
            n => n.EventId == claim.Id && n.NotificationType == "REMINDER_FIRED");
        Assert.Single(concrete.AuditSnapshot(f.Organization), a => a.Id == claim.Id && a.EventType == "REMINDER_FIRED");
        Assert.Equal(originalSequence + 1, await journal.GetRecipientSequenceAsync(f.Organization, f.Recipient, ct));
        Assert.True(await queue.CompleteAsync(f.Organization, retry.Id, retry.LeaseId, retry.WorkerId, ct));
    }
}
