using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private static async Task<(Guid Organization, Guid Recipient, CardRecord Card, CardReminder Reminder)> DemoAutomaticReminderFixture(
        ApiFactory app, HttpClient owner, HttpClient recipient, DemoQueueTestClock clock, CancellationToken ct)
    {
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Demo automatic reminder", null, null, clock.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        using var response = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders",
            new CardReminderInput("1_HOUR", true, 2, 0), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<CardReminderState>(ct);
        Assert.NotNull(state); Assert.NotNull(state.Reminder);
        return (f.Organization, f.Recipient, card, state.Reminder);
    }

    [Fact]
    public async Task ARCH_03_Demo_host_automatically_delivers_committed_reminder_without_external_dependencies()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        var delivered = new TaskCompletionSource<WorkEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = new ApiFactory(demoReminderDispatch: true, configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IWorkEventStore>(p => new DemoDeliveryAfterAppend(p.GetRequiredService<InMemoryWorkEventStore>(),
                source => delivered.TrySetResult(source)));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await DemoAutomaticReminderFixture(app, owner, recipient, clock, ct);
        Assert.True(app.Services.GetRequiredService<DemoCardReminderProcessingOptions>().Enabled);
        Assert.Null(app.Services.GetService<StrataAI.Infrastructure.Persistence.PostgresConnectionFactory>());
        var processing = app.Services.GetRequiredService<IDemoCardReminderProcessing>();
        Assert.False(await processing.AdvanceAsync(ct));
        clock.UtcNow = f.Reminder.TriggerAt!.Value;
        var source = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        // The processing gate waits through effect commit and job acknowledgment.
        Assert.False(await processing.AdvanceAsync(ct));
        clock.UtcNow = clock.UtcNow.AddMinutes(3);
        Assert.False(await processing.AdvanceAsync(ct));
        var reminder = await app.Services.GetRequiredService<ICardReminderStore>().FindAsync(f.Organization, f.Recipient, f.Card.Id, ct);
        Assert.NotNull(reminder); Assert.Equal("FIRED", reminder.Status);
        Assert.Equal(f.Reminder.Generation, reminder.Generation); Assert.Equal(f.Reminder.Version + 1, reminder.Version);
        Assert.Equal(source.CreatedAt, reminder.UpdatedAt);
        var notification = Assert.Single(await app.Services.GetRequiredService<IWorkNotificationStore>()
            .ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct), n => n.NotificationType == "REMINDER_FIRED");
        Assert.Equal(source.EventId, notification.Id); Assert.Equal(source.EventId, notification.EventId);
        Assert.Equal(f.Card.Id, notification.CardId); Assert.Equal(source.BoardId, notification.BoardId);
        Assert.Equal(source.CreatedAt, notification.CreatedAt); Assert.Equal(f.Recipient, notification.RecipientId);
        var audit = Assert.Single(app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(f.Organization),
            a => a.Id == source.EventId);
        Assert.Equal(source.CreatedAt, audit.CreatedAt); Assert.Equal(source.ActorId, audit.ActorId);
        Assert.Equal(source.EntityId, audit.EntityId); Assert.Equal(source.CorrelationId, audit.CorrelationId);
        Assert.Equal("REMINDER_FIRED", audit.EventType);
    }

    [Fact]
    public async Task ARCH_03_Demo_reminder_processing_serializes_real_concurrent_dispatch_and_preserves_one_effect()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await DemoAutomaticReminderFixture(app, owner, recipient, clock, ct);
        clock.UtcNow = f.Reminder.TriggerAt!.Value;
        var processing = app.Services.GetRequiredService<IDemoCardReminderProcessing>();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => processing.AdvanceAsync(ct)));
        Assert.Equal(1, results.Count(value => value));
        var notification = Assert.Single(await app.Services.GetRequiredService<IWorkNotificationStore>()
            .ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct), n => n.NotificationType == "REMINDER_FIRED");
        Assert.Single(app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(f.Organization),
            a => a.Id == notification.EventId);
        Assert.False(await processing.AdvanceAsync(ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_reminder_processing_retries_original_job_after_actual_effect_failure_and_backoff()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        var sources = new List<WorkEvent>(); var fail = true;
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IWorkEventStore>(p => new DemoDeliveryAfterAppend(p.GetRequiredService<InMemoryWorkEventStore>(), source =>
            {
                sources.Add(source);
                if (fail) { fail = false; throw new InvalidOperationException("Fixture effect failure before acknowledgment."); }
            }));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await DemoAutomaticReminderFixture(app, owner, recipient, clock, ct);
        var processing = app.Services.GetRequiredService<IDemoCardReminderProcessing>();
        var work = app.Services.GetRequiredService<InMemoryWorkManagementStore>();
        var before = work.AuditSnapshot(f.Organization);
        clock.UtcNow = f.Reminder.TriggerAt!.Value;
        Assert.True(await processing.AdvanceAsync(ct));
        var failed = Assert.Single(sources);
        Assert.False(app.Services.GetRequiredService<InMemoryWorkEventStore>().ContainsExact(failed));
        Assert.Equal(before, work.AuditSnapshot(f.Organization));
        Assert.Equal(f.Reminder, await app.Services.GetRequiredService<ICardReminderStore>().FindAsync(f.Organization, f.Recipient, f.Card.Id, ct));
        Assert.DoesNotContain(await app.Services.GetRequiredService<IWorkNotificationStore>()
            .ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct), n => n.NotificationType == "REMINDER_FIRED");
        clock.UtcNow = clock.UtcNow.AddSeconds(29); Assert.False(await processing.AdvanceAsync(ct));
        clock.UtcNow = clock.UtcNow.AddSeconds(1); Assert.True(await processing.AdvanceAsync(ct));
        Assert.Equal(2, sources.Count); Assert.Equal(sources[0].EventId, sources[1].EventId);
        Assert.True(app.Services.GetRequiredService<InMemoryWorkEventStore>().ContainsExact(sources[1]));
        Assert.Single(work.AuditSnapshot(f.Organization), a => a.Id == sources[1].EventId);
        Assert.Single(await app.Services.GetRequiredService<IWorkNotificationStore>()
            .ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct), n => n.EventId == sources[1].EventId);
        Assert.False(await processing.AdvanceAsync(ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_reminder_processor_preserves_other_job_types_for_their_actual_consumers()
    {
        var ct = TestContext.Current.CancellationToken; var actors = new OrganizationTransactionActorFixture();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<StrataAI.Application.Identity.ICommandActorAuthorization>(actors));
        using var client = app.CreateClient(); var org = Guid.NewGuid(); var actor = Guid.NewGuid();
        var jobs = app.Services.GetRequiredService<InMemoryBackgroundJobStore>();
        var job = new NewBackgroundJob(Guid.NewGuid(), org, "OTHER_DEMO_WORK", "retained-other-work", actor, "other-demo-service", "retained-other-work", "{}");
        var command = WorkCommand.Create(actor, Guid.NewGuid(), "DemoOtherJob", job.Id, new { }, "scope_unavailable");
        Assert.True((await app.Services.GetRequiredService<IWorkManagementUnitOfWork>().ExecuteAsync(org, command,
            _ => Task.FromResult(true), () => { jobs.Publish(job, ct); return Task.FromResult(WorkOperation<bool>.Success(true)); }, ct)).Succeeded);
        Assert.False(await app.Services.GetRequiredService<IDemoCardReminderProcessing>().AdvanceAsync(ct));
        var untouched = await jobs.ClaimAsync(org, Guid.NewGuid(), ct); Assert.NotNull(untouched);
        Assert.Equal(job.Id, untouched.Id); Assert.Equal(job.JobType, untouched.JobType); Assert.Equal(1, untouched.AttemptCount);
    }
}
