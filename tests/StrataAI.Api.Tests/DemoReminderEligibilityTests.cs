using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Theory]
    [InlineData("account")]
    [InlineData("organization-member")]
    [InlineData("board-member")]
    [InlineData("organization-deleting")]
    [InlineData("board-archived")]
    [InlineData("list-archived")]
    public async Task ARCH_03_Demo_reminder_revalidates_current_recipient_and_parents_despite_unchanged_publication(string change)
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IClock>(clock));
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await DemoAutomaticReminderFixture(app, owner, recipient, clock, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organizations = app.Services.GetRequiredService<IOrganizationStore>();
        var board = await work.FindBoardAsync(f.Card.BoardId, ct); Assert.NotNull(board);
        Assert.Equal(BoardVisibility.Private, board.Visibility);
        var member = await organizations.FindMembershipAsync(f.Organization, f.Recipient, ct);
        Assert.NotNull(member); Assert.Equal(OrganizationRole.Member, member.Role);
        // Actual canonical fixture transitions intentionally leave the old reminder
        // untouched. The delivery capability must independently refuse its effects.
        switch (change)
        {
            case "account":
                Assert.True(await app.Services.GetRequiredService<IIdentityStore>().DeactivateUserAsync(f.Recipient, clock.UtcNow, ct)); break;
            case "organization-member":
                Assert.Equal(OrganizationRemoveMemberResult.Removed, await organizations.RemoveMemberAsync(f.Organization, f.Recipient, clock.UtcNow, ct)); break;
            case "board-member":
                Assert.True(await work.RemoveBoardMemberAsync(board.Id, f.Recipient, clock.UtcNow, ct)); break;
            case "organization-deleting":
                var org = await organizations.FindOrganizationAsync(f.Organization, ct); Assert.NotNull(org);
                Assert.True(await organizations.MarkDeletingAsync(f.Organization, org.Version, clock.UtcNow, ct)); break;
            case "board-archived":
                Assert.NotNull(await work.SetBoardLifecycleAsync(board.Id, BoardLifecycleState.Active, BoardLifecycleState.Archived,
                    board.Version, clock.UtcNow, ct)); break;
            case "list-archived":
                var list = await work.FindListAsync(f.Card.ListId, ct); Assert.NotNull(list);
                Assert.NotNull(await work.SetListLifecycleAsync(list.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived,
                    list.Version, clock.UtcNow, ct)); break;
        }
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        Assert.Equal(f.Reminder, await reminders.FindAsync(f.Organization, f.Recipient, f.Card.Id, ct));
        var concrete = app.Services.GetRequiredService<InMemoryWorkManagementStore>();
        var audits = concrete.AuditSnapshot(f.Organization);
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var before = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        var journal = app.Services.GetRequiredService<INotificationRealtimeStore>();
        var sequence = await journal.GetRecipientSequenceAsync(f.Organization, f.Recipient, ct);
        clock.UtcNow = f.Reminder.TriggerAt!.Value;
        var processing = app.Services.GetRequiredService<IDemoCardReminderProcessing>();
        Assert.True(await processing.AdvanceAsync(ct));
        Assert.False(await processing.AdvanceAsync(ct));
        clock.UtcNow = clock.UtcNow.AddMinutes(3); Assert.False(await processing.AdvanceAsync(ct));
        Assert.Equal(f.Reminder, await reminders.FindAsync(f.Organization, f.Recipient, f.Card.Id, ct));
        Assert.Equal(audits, concrete.AuditSnapshot(f.Organization));
        Assert.Equal(before, await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
        Assert.Equal(sequence, await journal.GetRecipientSequenceAsync(f.Organization, f.Recipient, ct));
    }

    [Fact]
    public async Task ARCH_03_Demo_reminder_actual_delivery_failures_reach_terminal_limit_without_effects_or_new_claims()
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock(); var sources = new List<WorkEvent>();
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<IWorkEventStore>(p => new DemoDeliveryAfterAppend(p.GetRequiredService<InMemoryWorkEventStore>(), source =>
            {
                sources.Add(source); throw new InvalidOperationException("Fixture repeated failure after real reminder source.");
            }));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await DemoAutomaticReminderFixture(app, owner, recipient, clock, ct);
        var work = app.Services.GetRequiredService<InMemoryWorkManagementStore>(); var audits = work.AuditSnapshot(f.Organization);
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var before = await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct);
        var processing = app.Services.GetRequiredService<IDemoCardReminderProcessing>();
        clock.UtcNow = f.Reminder.TriggerAt!.Value;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(await processing.AdvanceAsync(ct)); Assert.Equal(attempt, sources.Count);
            Assert.Equal(sources[0].EventId, sources[^1].EventId);
            Assert.False(app.Services.GetRequiredService<InMemoryWorkEventStore>().ContainsExact(sources[^1]));
            Assert.Equal(f.Reminder, await app.Services.GetRequiredService<ICardReminderStore>().FindAsync(f.Organization, f.Recipient, f.Card.Id, ct));
            Assert.Equal(audits, work.AuditSnapshot(f.Organization));
            Assert.Equal(before, await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
            if (attempt < 5)
            {
                clock.UtcNow = clock.UtcNow.AddSeconds(30 * Math.Pow(2, attempt - 1) - 1);
                Assert.False(await processing.AdvanceAsync(ct));
                clock.UtcNow = clock.UtcNow.AddSeconds(1);
            }
        }
        // Inspect the actual retained terminal record; absence of a new claim
        // alone would not distinguish FAILED from a stranded PENDING record.
        var jobs = app.Services.GetRequiredService<StrataAI.Infrastructure.BackgroundJobs.InMemoryBackgroundJobStore>();
        var field = jobs.GetType().GetField("_rows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var rows = Assert.IsAssignableFrom<System.Collections.IDictionary>(field.GetValue(jobs));
        var stored = Assert.Single(rows.Values.Cast<object>(), row =>
            ((StrataAI.Application.BackgroundJobs.NewBackgroundJob)row.GetType().GetProperty("Job")!.GetValue(row)!).Id == sources[0].EventId);
        object? Value(string name)
        {
            var property = stored.GetType().GetProperty(name); Assert.NotNull(property);
            return property.GetValue(stored);
        }
        Assert.Equal("FAILED", Assert.IsType<string>(Value("State")));
        Assert.Equal(5, Assert.IsType<int>(Value("Attempts")));
        Assert.Equal(11, Assert.IsType<long>(Value("Version")));
        Assert.Equal(clock.UtcNow, Assert.IsType<DateTimeOffset>(Value("UpdatedAt")));
        Assert.Null(Value("Lease")); Assert.Null(Value("Worker")); Assert.Null(Value("LeaseExpiresAt"));
        var originalJob = Assert.IsType<StrataAI.Application.BackgroundJobs.NewBackgroundJob>(Value("Job"));
        Assert.Equal(f.Organization, originalJob.OrganizationId); Assert.Equal(f.Recipient, originalJob.ActorId);
        Assert.Equal(f.Reminder.Id, StrataAI.Application.WorkManagement.CardReminderAttempt.Parse(originalJob.SafeMetadataJson).ReminderId);
        Assert.Equal(f.Reminder.Generation, StrataAI.Application.WorkManagement.CardReminderAttempt.Parse(originalJob.SafeMetadataJson).Generation);
        clock.UtcNow = clock.UtcNow.AddDays(7);
        Assert.False(await processing.AdvanceAsync(ct));
        Assert.Null(await app.Services.GetRequiredService<StrataAI.Application.BackgroundJobs.IBackgroundJobStore>().ClaimAsync(f.Organization, Guid.NewGuid(), ct));
        Assert.Equal(5, sources.Count);
        Assert.Equal(audits, work.AuditSnapshot(f.Organization));
        Assert.Equal(before, await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct));
    }
}
