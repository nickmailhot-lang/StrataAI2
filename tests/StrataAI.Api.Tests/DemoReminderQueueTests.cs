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
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
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
}
