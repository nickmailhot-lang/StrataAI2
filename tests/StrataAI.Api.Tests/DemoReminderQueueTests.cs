using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class DemoQueueTestClock : IClock
    {
        private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;
        public DateTimeOffset UtcNow
        {
            get => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("actor")]
    [InlineData("exception")]
    [InlineData("cancel")]
    public async Task ARCH_03_Demo_real_Work_transaction_restores_reminder_publication_unless_committed(string outcome)
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        var actorFence = new OrganizationTransactionActorFixture();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock); services.AddSingleton<ICommandActorAuthorization>(actorFence);
        });
        using var client = app.CreateClient();
        var unit = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var publisher = app.Services.GetRequiredService<ICardReminderJobPublisher>();
        var queue = app.Services.GetRequiredService<IBackgroundJobStore>();
        var org = Guid.NewGuid(); var actor = Guid.NewGuid(); var now = clock.UtcNow;
        var reminder = new CardReminder(Guid.NewGuid(), org, actor, Guid.NewGuid(), "AT_DUE", true,
            now, now, "SCHEDULED", 1, now, now, 1);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var command = WorkCommand.Create(actor, Guid.NewGuid(), "DemoQueue", reminder.Id, new { }, "scope_unavailable");
        async Task<WorkOperation<bool>> Operation()
        {
            await publisher.PublishAsync(reminder, actor, "demo-transaction-test", cancel.Token);
            if (outcome == "actor") actorFence.Allowed = false;
            if (outcome == "exception") throw new InvalidOperationException("fixture failure after publication");
            if (outcome == "cancel") cancel.Cancel();
            return outcome == "failure" ? WorkOperation<bool>.Failure("fixture_refused") : WorkOperation<bool>.Success(true);
        }
        if (outcome == "exception")
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.ExecuteAsync(org, command, _ => Task.FromResult(true), Operation, cancel.Token));
        else if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unit.ExecuteAsync(org, command, _ => Task.FromResult(true), Operation, cancel.Token));
        else
            Assert.Equal(outcome == "success", (await unit.ExecuteAsync(org, command, _ => Task.FromResult(true), Operation, cancel.Token)).Succeeded);
        var claimed = await queue.ClaimAsync(org, Guid.NewGuid(), ct);
        Assert.Equal(outcome == "success", claimed is not null);
        // A refused command releases the gate and does not poison publication.
        actorFence.Allowed = true;
        var fresh = reminder with { Id = Guid.NewGuid() };
        var freshCommand = WorkCommand.Create(actor, Guid.NewGuid(), "DemoQueue", fresh.Id, new { }, "scope_unavailable");
        Assert.True((await unit.ExecuteAsync(org, freshCommand, _ => Task.FromResult(true), async () =>
        {
            await publisher.PublishAsync(fresh, actor, "demo-transaction-test", ct);
            return WorkOperation<bool>.Success(true);
        }, ct)).Succeeded);
        Assert.NotNull(await queue.ClaimAsync(org, Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_real_reminder_command_publishes_claimable_references_once_after_commit()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Demo queue reminder", null, null, clock.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        var path = $"/cards/{card.Id}/reminders";
        var input = new CardReminderInput("1_HOUR", true, 2, 0); var key = Guid.NewGuid().ToString();
        using var created = await Mutate(recipient, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var state = await created.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(state); Assert.NotNull(state.Reminder);
        using var replay = await Mutate(recipient, HttpMethod.Post, path, input, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(ct), await replay.Content.ReadAsStringAsync(ct));
        var queue = app.Services.GetRequiredService<IBackgroundJobStore>(); var worker = Guid.NewGuid();
        Assert.Null(await queue.ClaimAsync(f.Organization, worker, ct));
        clock.UtcNow = state.Reminder.TriggerAt!.Value;
        Assert.Null(await queue.ClaimAsync(Guid.NewGuid(), worker, ct));
        var claimed = await queue.ClaimAsync(f.Organization, worker, ct); Assert.NotNull(claimed);
        Assert.Equal(CardReminderDeliveryHandler.Type, claimed.JobType);
        Assert.Equal(CardReminderDeliveryHandler.Service, claimed.ServiceIdentity);
        Assert.Equal(f.Recipient, claimed.ActorId); Assert.Equal(1, claimed.AttemptCount);
        var references = CardReminderAttempt.Parse(claimed.SafeMetadataJson);
        Assert.Equal(state.Reminder.Id, references.ReminderId); Assert.Equal(state.Reminder.Generation, references.Generation);
        Assert.True(await queue.CompleteAsync(f.Organization, claimed.Id, claimed.LeaseId, worker, ct));
        Assert.Null(await queue.ClaimAsync(f.Organization, worker, ct));
    }

    // ARCH-03-AC-001 / ARCH-05-AC-001 / DATE-FR-007: publication is not delivery.
    [Fact]
    public async Task ARCH_03_Demo_real_reminder_claim_delivers_once_with_current_canonical_source()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Demo delivered reminder", null, null, clock.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        using var response = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders",
            new CardReminderInput("1_HOUR", true, 2, 0), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(state?.Reminder);
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        Assert.DoesNotContain(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct),
            n => n.NotificationType == "REMINDER_FIRED");
        clock.UtcNow = state.Reminder.TriggerAt!.Value;
        var queue = app.Services.GetRequiredService<IBackgroundJobStore>(); var worker = Guid.NewGuid();
        var claim = await queue.ClaimAsync(f.Organization, worker, ct); Assert.NotNull(claim);
        var delivery = app.Services.GetRequiredService<ICardReminderDeliveryStore>();
        var handler = new CardReminderDeliveryHandler(delivery);
        await handler.ExecuteAsync(claim, ct);
        await handler.ExecuteAsync(claim, ct); // Actual unacknowledged effect recovery.
        var fired = await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct);
        Assert.NotNull(fired); Assert.Equal("FIRED", fired.Status);
        Assert.Equal(state.Reminder.Generation, fired.Generation);
        Assert.Equal(state.Reminder.Version + 1, fired.Version);
        Assert.Equal(state.Reminder.CreatedAt, fired.CreatedAt);
        Assert.Equal(clock.UtcNow, fired.UpdatedAt);
        var notification = Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct),
            n => n.NotificationType == "REMINDER_FIRED");
        Assert.Equal(claim.Id, notification.EventId); Assert.Equal(claim.Id, notification.Id);
        Assert.Equal(f.Recipient, notification.RecipientId); Assert.Equal(claim.ActorId, notification.ActorId);
        Assert.Equal(card.Id, notification.CardId); Assert.Equal(card.BoardId, notification.BoardId);
        Assert.Equal(2, notification.CardVersion); Assert.Equal(clock.UtcNow, notification.CreatedAt);
        var audit = Assert.Single(Assert.IsType<StrataAI.Infrastructure.WorkManagement.InMemoryWorkManagementStore>(work)
            .AuditSnapshot(f.Organization), a => a.Id == claim.Id);
        Assert.Equal("REMINDER_FIRED", audit.EventType); Assert.Equal("Reminder", audit.EntityType);
        Assert.Equal(state.Reminder.Id, audit.EntityId); Assert.Equal(claim.ActorId, audit.ActorId);
        Assert.Equal(claim.CorrelationId, audit.CorrelationId); Assert.Equal(clock.UtcNow, audit.CreatedAt);
        Assert.True(await queue.CompleteAsync(f.Organization, claim.Id, claim.LeaseId, worker, ct));
        Assert.Equal(CardReminderDeliveryResult.LeaseLost,
            await delivery.DeliverAsync(claim, CardReminderAttempt.Parse(claim.SafeMetadataJson), ct));
        Assert.Null(await queue.ClaimAsync(f.Organization, worker, ct));
        Assert.Equal(fired, await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct));
        Assert.Single(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct),
            n => n.NotificationType == "REMINDER_FIRED");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("worker")]
    [InlineData("lease")]
    [InlineData("actor")]
    [InlineData("metadata")]
    [InlineData("expired")]
    [InlineData("generation")]
    [InlineData("complete")]
    public async Task ARCH_03_Demo_reminder_refuses_changed_claim_or_canonical_source_without_effects(string mode)
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Demo refused reminder", null, null, clock.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        using var response = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders",
            new CardReminderInput("1_HOUR", true, 2, 0), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(state); Assert.NotNull(state.Reminder);
        if (mode == "generation")
        {
            using var change = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders",
                new CardReminderInput("AT_DUE", true, 2, state.Reminder.Version), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }
        if (mode == "complete")
        {
            using var change = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
                new CardDatesInput(null, state.Reminder.DueAt!.Value.ToString("O"), "UTC", true, true, 2));
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }
        clock.UtcNow = state.Reminder.TriggerAt!.Value;
        var queue = app.Services.GetRequiredService<IBackgroundJobStore>();
        var claim = await queue.ClaimAsync(f.Organization, Guid.NewGuid(), ct); Assert.NotNull(claim);
        var altered = mode switch
        {
            "tenant" => claim with { OrganizationId = Guid.NewGuid() },
            "worker" => claim with { WorkerId = Guid.NewGuid() },
            "lease" => claim with { LeaseId = Guid.NewGuid() },
            "actor" => claim with { ActorId = Guid.NewGuid() },
            "metadata" => claim with { SafeMetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                { reminderId = state.Reminder.Id, generation = state.Reminder.Generation + 1 }) },
            _ => claim,
        };
        if (mode == "expired") clock.UtcNow = claim.LeaseExpiresAt;
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var beforeReminder = await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct);
        var beforeNotifications = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        var concrete = Assert.IsType<StrataAI.Infrastructure.WorkManagement.InMemoryWorkManagementStore>(work);
        var beforeAudits = concrete.AuditSnapshot(f.Organization);
        var delivery = app.Services.GetRequiredService<ICardReminderDeliveryStore>();
        Assert.Equal(mode is "generation" or "complete" ? CardReminderDeliveryResult.Superseded : CardReminderDeliveryResult.LeaseLost,
            await delivery.DeliverAsync(altered, CardReminderAttempt.Parse(altered.SafeMetadataJson), ct));
        Assert.Equal(beforeReminder, await reminders.FindAsync(f.Organization, f.Recipient, card.Id, ct));
        Assert.Equal(beforeNotifications, await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        Assert.Equal(beforeAudits, concrete.AuditSnapshot(f.Organization));
        Assert.DoesNotContain(await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct),
            n => n.NotificationType == "REMINDER_FIRED");
        if (mode is not ("expired" or "generation" or "complete"))
        {
            await new CardReminderDeliveryHandler(delivery).ExecuteAsync(claim, ct);
            Assert.True(await queue.CompleteAsync(f.Organization, claim.Id, claim.LeaseId, claim.WorkerId, ct));
        }
    }
}
