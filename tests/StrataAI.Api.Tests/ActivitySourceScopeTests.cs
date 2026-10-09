using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    [Fact]
    public async Task PRD_15_InternalActivityPlansAdmitHistoricalAndCurrentBoardsArchivesAndActualPrivateOwners()
    {
        var ct = TestContext.Current.CancellationToken; await using var app = new ApiFactory();
        using var owner = app.CreateClient(); using var member = app.CreateClient();
        var f = await NotificationFixture(app, owner, member, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var journal = app.Services.GetRequiredService<IWorkEventStore>();
        var sources = app.Services.GetRequiredService<IActivityEventSourceStore>();
        var plans = app.Services.GetRequiredService<ActivitySourceScopeResolver>();
        var transactions = app.Services.GetRequiredService<IWorkManagementUnitOfWork>();
        var at = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Protected activity", null, null, at, ct);
        async Task<T> Scope<T>(Func<Task<T>> action)
        {
            // Trusted fixture: this verifies Application entity/role scope, not
            // activity HTTP admission, session locks or PostgreSQL race behavior.
            var result = await transactions.ExecuteReadAsync(f.Organization, null, "fixture_denied", () => Task.FromResult(true),
                async () => WorkOperation<T>.Success(await action()), ct);
            Assert.True(result.Succeeded); return result.Value!;
        }
        async Task<ActivityEventSource> Publish(WorkEvent change)
            => await Scope(async () =>
            {
                await journal.AppendAsync(change, ct);
                return Assert.Single(await sources.ReadBoardWindowAsync(f.Organization, change.BoardId, null, null, ct), row => row.EventId == change.EventId);
            });
        Task<ActivitySourceScope?> Resolve(ActivityEventSource source, Guid viewer)
            => Scope(() => plans.ResolveAsync(source, viewer, ct));
        var source = await Publish(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "CARD_UPDATED", "Card", card.Id, 1, "activity-scope", at));
        var original = await Resolve(source, f.Recipient);
        Assert.NotNull(original); Assert.Equal(f.Board, original.SourceBoardId); Assert.Equal(f.Board, original.CurrentBoardId);
        Assert.Equal(f.List, original.ParentListId); Assert.Null(original.PrivateOwnerId);
        await Scope(async () =>
        {
            var read = plans.CreateReadPass(f.Recipient);
            Assert.Equal(original, await read(source, ct));
            var repeated = source with { EventId = Guid.NewGuid() };
            Assert.Equal(original with { EventId = repeated.EventId }, await read(repeated, ct));
            Assert.Null(await read(source with { ActorId = Guid.Empty }, ct));
            Assert.Null(await read(source with { Version = 0 }, ct));
            Assert.Null(await read(source with { EventType = "WATCH_CREATED" }, ct));
            Assert.Null(await read(source with { OrganizationId = Guid.NewGuid() }, ct));
            return true;
        });
        Assert.Null(await Resolve(source with { EntityType = "Unknown" }, f.Owner));
        Assert.Null(await Resolve(source with { EventType = "WATCH_CREATED" }, f.Owner));
        Assert.Null(await Resolve(source with { EntityType = "Board", EntityId = Guid.NewGuid() }, f.Owner));
        Assert.Null(await Resolve(source, Guid.Empty));

        using var foreignOrgCreated = await Mutate(owner, HttpMethod.Post, "/organizations", new { name = "Foreign activity Organization" });
        Assert.Equal(HttpStatusCode.Created, foreignOrgCreated.StatusCode);
        var foreignOrg = (await foreignOrgCreated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("organization").GetProperty("id").GetGuid();
        using var foreignBoardCreated = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = foreignOrg, name = "Foreign activity Board" });
        Assert.Equal(HttpStatusCode.Created, foreignBoardCreated.StatusCode);
        var foreignBoard = (await foreignBoardCreated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        using var foreignListCreated = await Mutate(owner, HttpMethod.Post, $"/boards/{foreignBoard}/lists", new { name = "Foreign activity List" });
        Assert.Equal(HttpStatusCode.Created, foreignListCreated.StatusCode);
        var foreignList = (await foreignListCreated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var foreignCard = await work.CreateCardAsync(foreignList, Guid.NewGuid(), "Foreign activity Card", null, null, at, ct);
        // Even an administrator of both Organizations cannot interpret a
        // foreign current entity as part of this source tenant's activity.
        Assert.Null(await Resolve(source with { EntityId = foreignCard.Id }, f.Owner));

        using var pastCreated = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Historical source Board" });
        Assert.Equal(HttpStatusCode.Created, pastCreated.StatusCode);
        var pastBoard = (await pastCreated.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        // Synthetic past source with a real current Card/Board. This is not an
        // executed cross-Board Card move or a replacement for that acceptance.
        var historical = await Publish(new(Guid.NewGuid(), f.Organization, pastBoard, f.Owner, "CARD_UPDATED", "Card", card.Id, 1, "past-activity-scope", at));
        Assert.Null(await Resolve(historical, f.Recipient));
        var ownerPast = await Resolve(historical, f.Owner);
        Assert.NotNull(ownerPast); Assert.Equal(pastBoard, ownerPast.SourceBoardId); Assert.Equal(f.Board, ownerPast.CurrentBoardId);
        using var pastGrant = await Mutate(owner, HttpMethod.Patch, $"/boards/{pastBoard}/members/{f.Recipient}", new { role = "MEMBER" });
        Assert.Equal(HttpStatusCode.OK, pastGrant.StatusCode);
        Assert.NotNull(await Resolve(historical, f.Recipient));
        await work.RemoveBoardMemberAsync(pastBoard, f.Recipient, at.AddSeconds(1), ct);
        Assert.Null(await Resolve(historical, f.Recipient));
        Assert.NotNull(await Resolve(source, f.Recipient));

        var watches = app.Services.GetRequiredService<IWatchSubscriptionStore>();
        var reminders = app.Services.GetRequiredService<ICardReminderStore>();
        var watch = await Scope(() => watches.SetAsync(f.Organization, f.Recipient, "CARD", card.Id, true, 0, at, ct));
        var reminder = await Scope(() => reminders.SetAsync(card with { DueAt = at.AddDays(2) }, f.Recipient, "1_DAY", true, 0, at, ct));
        Assert.NotNull(watch); Assert.NotNull(reminder);
        var watchSource = await Publish(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "WATCH_CREATED", "WatchSubscription", watch.Id, watch.Version, "private-watch-scope", at));
        var reminderSource = await Publish(new(Guid.NewGuid(), f.Organization, f.Board, f.Owner, "REMINDER_SCHEDULED", "Reminder", reminder.Id, reminder.Version, "private-reminder-scope", at));
        Assert.Null(await Resolve(watchSource, f.Owner)); Assert.Null(await Resolve(reminderSource, f.Owner));
        var personal = await Resolve(reminderSource, f.Recipient);
        Assert.NotNull(personal); Assert.Equal(f.Recipient, personal.PrivateOwnerId); Assert.Equal(card.Id, personal.TargetId);
        Assert.NotNull(await Resolve(watchSource, f.Recipient));
        Assert.Null(await Resolve(watchSource with { EventId = Guid.NewGuid() }, f.Recipient));
        Assert.Null(await Resolve(reminderSource with { EventId = source.EventId }, f.Recipient));
        await Scope(async () =>
        {
            var read = plans.CreateReadPass(f.Recipient);
            Assert.NotNull(await read(watchSource, ct));
            Assert.Null(await read(watchSource with { EventId = Guid.NewGuid() }, ct));
            Assert.NotNull(await read(reminderSource, ct));
            Assert.Null(await read(reminderSource with { EventId = Guid.NewGuid() }, ct));
            return true;
        });
        await Scope(() => watches.SetAsync(f.Organization, f.Recipient, "CARD", card.Id, false, watch.Version, at.AddSeconds(1), ct));
        await Scope(() => reminders.SetAsync(card, f.Recipient, "1_DAY", false, reminder.Version, at.AddSeconds(1), ct));
        Assert.NotNull(await Resolve(watchSource, f.Recipient)); Assert.NotNull(await Resolve(reminderSource, f.Recipient));

        using var archived = await Mutate(owner, HttpMethod.Post, $"/cards/{card.Id}/archive", new { version = 1 });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.NotNull(await Resolve(source, f.Recipient)); Assert.NotNull(await Resolve(reminderSource, f.Recipient));
        using var deleted = await Mutate(owner, HttpMethod.Delete, $"/cards/{card.Id}?version=2&confirmed=true", new { });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Null(await Resolve(source, f.Recipient)); Assert.Null(await Resolve(reminderSource, f.Recipient));
        Assert.NotNull(await Resolve(source, f.Owner));
        await Scope(async () =>
        {
            var fresh = plans.CreateReadPass(f.Recipient);
            Assert.Null(await fresh(source, ct));
            Assert.Null(await fresh(reminderSource, ct));
            return true;
        });
        await work.RemoveBoardMemberAsync(f.Board, f.Recipient, at.AddSeconds(2), ct);
        Assert.Null(await Resolve(watchSource, f.Recipient));
    }
}
