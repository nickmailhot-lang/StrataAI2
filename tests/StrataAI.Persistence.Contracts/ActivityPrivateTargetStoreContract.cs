using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;

internal static class ActivityPrivateTargetStoreContract
{
    private static void Require(bool condition, string invariant)
    { if (!condition) throw new InvalidOperationException(invariant); }

    public static async Task RunAsync(IServiceProvider provider, Guid tenant, Guid foreignTenant, Guid owner,
        Guid differentActor, Guid cardId, CancellationToken ct)
    {
        var targets = provider.GetRequiredService<IActivityPrivateTargetStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        var work = provider.GetRequiredService<IWorkManagementStore>();
        var events = provider.GetRequiredService<IWorkEventStore>();
        var watches = provider.GetRequiredService<IWatchSubscriptionStore>();
        var reminders = provider.GetRequiredService<ICardReminderStore>();
        async Task<T> Scope<T>(Guid org, Func<Task<T>> action)
        {
            var result = await unit.ExecuteReadAsync(org, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await action()), ct);
            Require(result.Succeeded, "Private activity owning operation failed."); return result.Value!;
        }
        var watchEvent = Guid.NewGuid(); var reminderEvent = Guid.NewGuid();
        try { await targets.FindAsync(tenant, watchEvent, ct); throw new InvalidOperationException("Unowned private activity target read accepted."); }
        catch (InvalidOperationException exception) when (exception.Message == "Activity targets require the owning Work transaction.") { }
        await Scope(tenant, async () =>
        {
            var card = await work.FindCardAsync(cardId, ct) ?? throw new InvalidOperationException("Private activity Card fixture missing.");
            var at = AttachmentMetadataMapping.DatabaseTimestamp(DateTimeOffset.UtcNow);
            var oldWatch = await watches.FindAsync(tenant, owner, "CARD", cardId, ct);
            var watch = await watches.SetAsync(tenant, owner, "CARD", cardId, true, oldWatch?.Version ?? 0, at, ct)
                ?? throw new InvalidOperationException("Private activity watch fixture failed.");
            var oldReminder = await reminders.FindAsync(tenant, owner, cardId, ct);
            var reminder = await reminders.SetAsync(card, owner, "AT_DUE", true, oldReminder?.Version ?? 0, at, ct)
                ?? throw new InvalidOperationException("Private activity Reminder fixture failed.");
            // A synthetic different actor tests audience identity, not command admission.
            await events.AppendAsync(new(watchEvent, tenant, card.BoardId, differentActor, "WATCH_CREATED", "WatchSubscription", watch.Id, watch.Version, "private-watch-source", at), ct);
            await events.AppendAsync(new(reminderEvent, tenant, card.BoardId, differentActor,
                reminder.Status == "SCHEDULED" ? "REMINDER_SCHEDULED" : "REMINDER_CANCELLED", "Reminder", reminder.Id,
                reminder.Version, "private-reminder-source", at), ct);
            var expected = new ActivityPrivateTarget(owner, "CARD", cardId);
            Require(await targets.FindAsync(tenant, watchEvent, ct) == expected && await targets.FindAsync(tenant, reminderEvent, ct) == expected,
                "Private activity audience followed its actor instead of its stored owner/parent.");
            Require(await targets.FindAsync(tenant, watch.Id, ct) is null && await targets.FindAsync(tenant, Guid.NewGuid(), ct) is null,
                "Private activity target lookup accepted an unrecorded source identity.");
            var normal = new WorkEvent(Guid.NewGuid(), tenant, card.BoardId, owner, "CARD_UPDATED", "Card", cardId, card.Version, "nonprivate-activity-source", at);
            await events.AppendAsync(normal, ct);
            Require(await targets.FindAsync(tenant, normal.EventId, ct) is null, "Shared Card source acquired a private audience.");
            Require(await watches.SetAsync(tenant, owner, "CARD", cardId, false, watch.Version, at.AddSeconds(1), ct) is not null,
                "Private activity watch removal fixture failed.");
            Require(await reminders.SetAsync(card, owner, "AT_DUE", false, reminder.Version, at.AddSeconds(1), ct) is not null,
                "Private activity Reminder cancellation fixture failed.");
            Require(await targets.FindAsync(tenant, watchEvent, ct) == expected && await targets.FindAsync(tenant, reminderEvent, ct) == expected,
                "Private historical target vanished after watch removal/Reminder cancellation.");
            return true;
        });
        Require(await Scope(foreignTenant, () => targets.FindAsync(foreignTenant, watchEvent, ct)) is null &&
            await Scope(foreignTenant, () => targets.FindAsync(foreignTenant, reminderEvent, ct)) is null,
            "Private activity target widened across tenants.");
        Console.WriteLine("Real restricted private activity targets: actual source identity, persisted owner distinct from actor, tenant/parent affinity and historical interpretation after watch removal/Reminder cancellation passed. Raw target hints do not establish audience admission.");
    }
}
