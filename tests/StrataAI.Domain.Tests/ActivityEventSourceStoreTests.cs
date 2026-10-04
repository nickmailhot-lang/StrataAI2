using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Runtime;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Identity;
using StrataAI.Infrastructure.Organizations;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class ActivityEventSourceStoreTests
{
    private sealed class FixtureActorAuthorization : ICommandActorAuthorization
    {
        public Task<bool> VerifyAsync(Guid actorId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task PRD_15_InternalActivityWindowsRetainHistoricalCaptionsStableTiesTenantScopeAndRollback()
    {
        var ct = TestContext.Current.CancellationToken; var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        var registrations = new ServiceCollection(); registrations.AddSingleton<IClock, SystemClock>();
        registrations.AddSingleton<ICommandActorAuthorization>(new FixtureActorAuthorization());
        registrations.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        registrations.AddStrataAiOrganizations(runtime); registrations.AddStrataAiWorkManagement(runtime);
        using var provider = registrations.BuildServiceProvider();
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var actor = Guid.NewGuid(); var card = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero); var email = $"{actor:N}@example.test";
        var identity = provider.GetRequiredService<IIdentityStore>();
        Assert.True(await identity.TryCreateUserAsync(new(actor, email, email.ToUpperInvariant(), "Original actor", null,
            "en", "UTC", AccountStatus.Active, true, "fixture", at, at, 1), null, null, ct));
        await provider.GetRequiredService<IOrganizationStore>().CreateOrganizationAsync(actor, org, "Activity", null, at, ct);
        var sources = provider.GetRequiredService<IActivityEventSourceStore>(); var events = provider.GetRequiredService<IWorkEventStore>();
        var unit = provider.GetRequiredService<IWorkManagementUnitOfWork>();
        async Task<T> Scope<T>(Guid tenant, Func<Task<T>> action)
        {
            var result = await unit.ExecuteReadAsync(tenant, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await action()), ct);
            Assert.True(result.Succeeded); return result.Value!;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.ReadBoardWindowAsync(org, board, null, null, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.ReadCardWindowAsync(org, board, card, null, null, ct));
        var originals = new List<WorkEvent>();
        await Scope(org, async () =>
        {
            for (var index = 1; index <= 65; index++)
            {
                var source = new WorkEvent(Guid.Parse($"06400000-0000-0000-0000-{index:D12}"), org, board, actor,
                    "CARD_UPDATED", "Card", card, index, "activity-fixture", at);
                originals.Add(source); await events.AppendAsync(source, ct);
            }
            return true;
        });
        var first = await Scope(org, () => sources.ReadBoardWindowAsync(org, board, null, null, ct));
        Assert.Equal(51, first.Count); Assert.All(first, row => { Assert.Equal("Original actor", row.ActorLabel); Assert.Empty(row.Metadata); });
        var anchor = first[49]; var tail = await Scope(org, () => sources.ReadBoardWindowAsync(org, board, anchor.CreatedAt, anchor.EventId, ct));
        Assert.Equal(15, tail.Count); Assert.Equal(65, first.Take(50).Concat(tail).Select(row => row.EventId).Distinct().Count());
        Assert.Equal(first, await Scope(org, () => sources.ReadBoardWindowAsync(org, board, null, null, ct)));
        var otherBoard = Guid.NewGuid();
        await Scope(org, async () =>
        {
            await events.AppendAsync(originals[0] with { EventId = Guid.NewGuid(), EntityType = "Board", EntityId = board, CreatedAt = at.AddSeconds(1) }, ct);
            await events.AppendAsync(originals[0] with { EventId = Guid.NewGuid(), EntityId = Guid.NewGuid(), CreatedAt = at.AddSeconds(1) }, ct);
            await events.AppendAsync(originals[0] with { EventId = Guid.NewGuid(), BoardId = otherBoard, CreatedAt = at.AddSeconds(1) }, ct);
            return true;
        });
        var cardFirst = await Scope(org, () => sources.ReadCardWindowAsync(org, board, card, null, null, ct));
        Assert.Equal(first, cardFirst);
        var cardTail = await Scope(org, () => sources.ReadCardWindowAsync(org, board, card, anchor.CreatedAt, anchor.EventId, ct));
        Assert.Equal(tail, cardTail);
        Assert.Single(await Scope(org, () => sources.ReadCardWindowAsync(org, otherBoard, card, null, null, ct)));
        Assert.Empty(await Scope(org, () => sources.ReadCardWindowAsync(org, board, Guid.NewGuid(), null, null, ct)));
        await Assert.ThrowsAsync<ArgumentException>(() => Scope(org, () => sources.ReadCardWindowAsync(org, board, Guid.Empty, null, null, ct)));
        await Assert.ThrowsAsync<ArgumentException>(() => Scope(org, () => sources.ReadBoardWindowAsync(org, board, at, null, ct)));
        await Assert.ThrowsAsync<ArgumentException>(() => Scope(org, () => sources.ReadBoardWindowAsync(org, board, at.AddTicks(1), anchor.EventId, ct)));
        await Assert.ThrowsAsync<ArgumentException>(() => Scope(org, () => sources.ReadBoardWindowAsync(org, board, at.ToOffset(TimeSpan.FromHours(1)), anchor.EventId, ct)));
        var metadata = Assert.IsAssignableFrom<IDictionary<string, object?>>(first[0].Metadata);
        Assert.Throws<NotSupportedException>(() => metadata.Add("body", "Private comment"));
        var copy = first[0] with { ActorLabel = "Changed view" };
        Assert.NotEqual(copy, (await Scope(org, () => sources.ReadCardWindowAsync(org, board, card, null, null, ct)))[0]);
        Assert.NotNull(await identity.UpdateProfileAsync(actor, "Later actor", null, "en", "UTC", 1, at.AddSeconds(1), ct));
        Assert.True(await identity.DeactivateUserAsync(actor, at.AddSeconds(2), ct));
        await Scope(org, async () => { await events.AppendAsync(originals[0], ct); return true; });
        Assert.All(await Scope(org, () => sources.ReadBoardWindowAsync(org, board, null, null, ct)), row => Assert.Equal("Original actor", row.ActorLabel));
        var failed = new WorkEvent(Guid.NewGuid(), org, board, actor, "COMMENT_ADDED", "Card", Guid.NewGuid(), 1, "activity-rollback", at.AddSeconds(3));
        var refusal = await unit.ExecuteReadAsync(org, null, "fixture_denied", () => Task.FromResult(true), async () =>
        {
            await events.AppendAsync(failed, ct); return WorkOperation<bool>.Failure("fixture_refused");
        }, ct);
        Assert.Equal("fixture_refused", refusal.ErrorCode);
        Assert.DoesNotContain(await Scope(org, () => sources.ReadBoardWindowAsync(org, board, null, null, ct)), row => row.EventId == failed.EventId);
        await Scope(org, async () => { await events.AppendAsync(failed, ct); return true; });
        Assert.Equal("Later actor", (await Scope(org, () => sources.ReadBoardWindowAsync(org, board, null, null, ct)))[0].ActorLabel);
        var foreign = Guid.NewGuid();
        await Scope(foreign, async () => { await events.AppendAsync(failed with { EventId = Guid.NewGuid(), OrganizationId = foreign }, ct); return true; });
        Assert.Equal($"Member {actor:D}", Assert.Single(await Scope(foreign, () => sources.ReadBoardWindowAsync(foreign, board, null, null, ct))).ActorLabel);
        Assert.Empty(await Scope(org, () => sources.ReadBoardWindowAsync(org, Guid.NewGuid(), null, null, ct)));
        var targets = provider.GetRequiredService<IActivityPrivateTargetStore>();
        var watches = provider.GetRequiredService<IWatchSubscriptionStore>();
        var reminders = provider.GetRequiredService<ICardReminderStore>();
        var personalOwner = Guid.NewGuid();
        var reminderCard = new CardRecord(card, org, board, Guid.NewGuid(), "Activity", null, "rank",
            WorkItemLifecycleState.Active, at, at, 1) { DueAt = at.AddDays(2), DueTimezone = "UTC" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => targets.FindAsync(org, originals[0].EventId, ct));
        Guid watchEvent = Guid.NewGuid(); Guid reminderEvent = Guid.NewGuid();
        await Scope(org, async () =>
        {
            var watch = await watches.SetAsync(org, personalOwner, "CARD", card, true, 0, at, ct);
            var reminder = await reminders.SetAsync(reminderCard, personalOwner, "1_DAY", true, 0, at, ct);
            Assert.NotNull(watch); Assert.NotNull(reminder);
            await events.AppendAsync(new(watchEvent, org, board, actor, "WATCH_CREATED", "WatchSubscription", watch.Id, watch.Version, "private-watch-source", at), ct);
            await events.AppendAsync(new(reminderEvent, org, board, actor, "REMINDER_SCHEDULED", "Reminder", reminder.Id, reminder.Version, "private-reminder-source", at), ct);
            var expected = new ActivityPrivateTarget(personalOwner, "CARD", card);
            Assert.Equal(expected, await targets.FindAsync(org, watchEvent, ct));
            Assert.Equal(expected, await targets.FindAsync(org, reminderEvent, ct));
            Assert.Null(await targets.FindAsync(org, watch.Id, ct));
            Assert.Null(await targets.FindAsync(org, originals[0].EventId, ct));
            Assert.NotNull(await watches.SetAsync(org, personalOwner, "CARD", card, false, watch.Version, at.AddSeconds(1), ct));
            Assert.NotNull(await reminders.SetAsync(reminderCard, personalOwner, "1_DAY", false, reminder.Version, at.AddSeconds(1), ct));
            Assert.Equal(expected, await targets.FindAsync(org, watchEvent, ct));
            Assert.Equal(expected, await targets.FindAsync(org, reminderEvent, ct));
            return true;
        });
        Assert.Null(await Scope(foreign, () => targets.FindAsync(foreign, reminderEvent, ct)));
    }
}
