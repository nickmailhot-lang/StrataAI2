using System.Text.Json;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class CardReminderDeliveryTests
{
    private static CardReminder Reminder()
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1_HOUR", true,
            now.AddHours(2), now.AddHours(1), "SCHEDULED", 3, now, now, 4);
    }
    private static ClaimedBackgroundJob Claim() => new(Guid.NewGuid(), Guid.NewGuid(), CardReminderDeliveryHandler.Type,
        Guid.NewGuid(), CardReminderDeliveryHandler.Service, "reminder-test",
        JsonSerializer.Serialize(new { reminderId = Guid.NewGuid(), generation = 3 }), 1,
        Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2));

    [Fact]
    public void Scheduled_job_has_only_canonical_reference_and_generation_and_is_available_at_exact_UTC_trigger()
    {
        var reminder = Reminder(); var actor = Guid.NewGuid();
        var first = CardReminderJobs.Create(reminder, actor, "reminder-test");
        var retry = CardReminderJobs.Create(reminder, actor, "reminder-test");
        Assert.NotEqual(first.Id, retry.Id); Assert.Equal(first.IdempotencyKey, retry.IdempotencyKey);
        Assert.Equal(reminder.OrganizationId, first.OrganizationId); Assert.Equal(actor, first.ActorId);
        Assert.Equal(reminder.TriggerAt, first.AvailableAt); Assert.Equal(TimeSpan.Zero, first.AvailableAt!.Value.Offset);
        Assert.Equal(new(reminder.Id, reminder.Generation), CardReminderAttempt.Parse(first.SafeMetadataJson));
        Assert.NotEqual(first.IdempotencyKey, CardReminderJobs.Create(reminder with { Generation = 4 }, actor, "reminder-test").IdempotencyKey);
        Assert.Equal(CardReminderDeliveryHandler.Type, first.JobType); Assert.Equal(CardReminderDeliveryHandler.Service, first.ServiceIdentity);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("suspended")]
    [InlineData("fired")]
    [InlineData("trigger")]
    [InlineData("interval")]
    [InlineData("generation")]
    [InlineData("version")]
    [InlineData("due")]
    public void Invalid_or_inactive_plans_cannot_create_jobs(string invalid)
    {
        var row = Reminder();
        row = invalid switch
        {
            "disabled" => row with { Enabled = false }, "suspended" => row with { Status = "SUSPENDED" },
            "fired" => row with { Status = "FIRED" }, "trigger" => row with { TriggerAt = row.TriggerAt!.Value.AddSeconds(1) },
            "interval" => row with { IntervalCode = "unknown" }, "generation" => row with { Generation = 0 },
            "version" => row with { Version = 2 }, _ => row with { DueAt = null }
        };
        Assert.Throws<InvalidOperationException>(() => CardReminderJobs.Create(row, Guid.NewGuid(), "test"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"reminderId\":1,\"generation\":1}")]
    [InlineData("{\"reminderId\":\"00000000-0000-0000-0000-000000000000\",\"generation\":1}")]
    [InlineData("{\"reminderId\":\"11111111-1111-1111-1111-111111111111\",\"generation\":0}")]
    [InlineData("{\"reminderId\":\"11111111-1111-1111-1111-111111111111\",\"generation\":1.5}")]
    [InlineData("{\"reminderId\":\"11111111-1111-1111-1111-111111111111\",\"generation\":\"1\"}")]
    [InlineData("{\"reminderId\":\"11111111-1111-1111-1111-111111111111\",\"generation\":9223372036854775808}")]
    [InlineData("{\"reminderId\":\"11111111-1111-1111-1111-111111111111\",\"generation\":1,\"recipient\":\"forged\"}")]
    [InlineData("{\"reminderId\":\"11111111-1111-1111-1111-111111111111\",\"reminderId\":\"22222222-2222-2222-2222-222222222222\"}")]
    public async Task Forged_or_malformed_metadata_never_reaches_delivery(string metadata)
    {
        var store = new Store();
        await Assert.ThrowsAnyAsync<Exception>(() => new CardReminderDeliveryHandler(store)
            .ExecuteAsync(Claim() with { SafeMetadataJson = metadata }, TestContext.Current.CancellationToken));
        Assert.Null(store.Job);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("service")]
    [InlineData("job")]
    [InlineData("organization")]
    [InlineData("actor")]
    [InlineData("lease")]
    [InlineData("worker")]
    public async Task Invalid_claim_scope_cannot_call_delivery(string invalid)
    {
        var job = Claim();
        job = invalid switch
        {
            "type" => job with { JobType = "OTHER" }, "service" => job with { ServiceIdentity = "other" },
            "job" => job with { Id = Guid.Empty }, "organization" => job with { OrganizationId = Guid.Empty },
            "actor" => job with { ActorId = Guid.Empty }, "lease" => job with { LeaseId = Guid.Empty },
            _ => job with { WorkerId = Guid.Empty }
        };
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CardReminderDeliveryHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken));
        Assert.Null(store.Job);
    }

    [Theory]
    [InlineData(CardReminderDeliveryResult.Delivered)]
    [InlineData(CardReminderDeliveryResult.Superseded)]
    public async Task Current_delivery_or_obsolete_generation_can_be_acknowledged_without_another_effect(CardReminderDeliveryResult outcome)
    {
        var job = Claim(); var store = new Store { Result = outcome };
        await new CardReminderDeliveryHandler(store).ExecuteAsync(job, TestContext.Current.CancellationToken);
        Assert.Same(job, store.Job); Assert.Equal(CardReminderAttempt.Parse(job.SafeMetadataJson), store.Attempt);
    }

    [Fact]
    public async Task Lost_lease_is_not_acknowledged_and_cancellation_cannot_start_a_delivery()
    {
        var store = new Store { Result = CardReminderDeliveryResult.LeaseLost };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CardReminderDeliveryHandler(store).ExecuteAsync(Claim(), TestContext.Current.CancellationToken));
        store = new Store(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new CardReminderDeliveryHandler(store).ExecuteAsync(Claim(), cancellation.Token));
        Assert.Null(store.Job);
    }

    private sealed class Store : ICardReminderDeliveryStore
    {
        public CardReminderDeliveryResult Result { get; init; } = CardReminderDeliveryResult.Delivered;
        public ClaimedBackgroundJob? Job { get; private set; }
        public CardReminderAttempt? Attempt { get; private set; }
        public Task<CardReminderDeliveryResult> DeliverAsync(ClaimedBackgroundJob job, CardReminderAttempt attempt, CancellationToken ct)
        {
            Job = job; Attempt = attempt; return Task.FromResult(Result);
        }
    }
}
