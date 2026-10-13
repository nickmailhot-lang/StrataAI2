using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.WorkManagement;
using Xunit;

namespace StrataAI.Api.Tests;

public sealed partial class ApiHostTests
{
    private sealed class ListMovementPublicationEvents(InMemoryWorkEventStore inner) : IWorkEventStore
    {
        public Guid? FailBoard { get; set; }
        public bool Capture { get; set; }
        public List<WorkEvent> Sources { get; } = [];
        public async Task AppendAsync(WorkEvent source, CancellationToken ct = default)
        {
            await inner.AppendAsync(source, ct);
            if (Capture) Sources.Add(source);
            if (source.EventType == "LIST_MOVED" && source.BoardId == FailBoard)
                throw new InvalidOperationException("Fixture late List publication failure.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PRD_07_List_publication_rolls_back_all_effects_then_replays_original_receipt(bool eligible)
    {
        var ct = TestContext.Current.CancellationToken; var clock = new DemoQueueTestClock();
        var context = new ListMovementCommandContext { IdempotencyKey = null };
        ListMovementPublicationEvents publication = null!;
        await using var app = new ApiFactory(configureServices: services =>
        {
            services.AddSingleton<IClock>(clock);
            services.AddSingleton<ICommandActorAuthorization>(new OrganizationTransactionActorFixture());
            services.AddSingleton<IWorkCommandContext>(context);
            services.AddSingleton<IWorkEventStore>(p => publication = new(p.GetRequiredService<InMemoryWorkEventStore>()));
        });
        using var owner = app.CreateClient(); using var recipient = app.CreateClient();
        var f = await NotificationFixture(app, owner, recipient, ct);
        var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var concrete = Assert.IsType<InMemoryWorkManagementStore>(work);
        using var created = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = f.Organization, name = "Publication target", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var destination = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        if (eligible) Assert.NotNull(await work.UpsertBoardMemberAsync(destination, f.Recipient, BoardRole.Member, clock.UtcNow, ct));
        var card = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Reminder and watch subject", "Stable body", RankToken.Initial(), clock.UtcNow, ct);
        using var dates = await Mutate(owner, HttpMethod.Patch, $"/cards/{card.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, dates.StatusCode);
        using var reminderReply = await Mutate(recipient, HttpMethod.Post, $"/cards/{card.Id}/reminders", new CardReminderInput("1_HOUR", true, 2, 0));
        Assert.Equal(HttpStatusCode.OK, reminderReply.StatusCode);
        var reminder = (await reminderReply.Content.ReadFromJsonAsync<CardReminderState>(ct))!.Reminder!;
        foreach (var (type, id) in new[] { ("CARD", card.Id), ("LIST", f.List), ("BOARD", f.Board) })
        {
            using var watched = await Mutate(recipient, HttpMethod.Put, $"/watch/{type}/{id}?version=0", new { });
            Assert.Equal(HttpStatusCode.OK, watched.StatusCode);
        }
        var archived = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Archived subject", null, RankToken.Initial(), clock.UtcNow, ct);
        using var archivedDates = await Mutate(owner, HttpMethod.Patch, $"/cards/{archived.Id}/dates",
            new CardDatesInput(null, clock.UtcNow.AddDays(2).ToString("O"), "UTC", true, false, 1));
        Assert.Equal(HttpStatusCode.OK, archivedDates.StatusCode);
        using var archivedReminderReply = await Mutate(recipient, HttpMethod.Post, $"/cards/{archived.Id}/reminders", new CardReminderInput("1_HOUR", true, 2, 0));
        Assert.Equal(HttpStatusCode.OK, archivedReminderReply.StatusCode);
        var archivedReminder = (await archivedReminderReply.Content.ReadFromJsonAsync<CardReminderState>(ct))!.Reminder!;
        // Seed a retained stale scheduled reminder to prove movement repairs
        // hidden-child eligibility rather than assuming all children are active.
        archived = (await work.SetCardLifecycleAsync(archived.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 2, clock.UtcNow, ct))!;
        var deleted = await work.CreateCardAsync(f.List, Guid.NewGuid(), "Deleted subject", null, RankToken.Initial(), clock.UtcNow, ct);
        Assert.NotNull(await work.SetCardLifecycleAsync(deleted.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, clock.UtcNow, ct));
        deleted = (await work.SetCardLifecycleAsync(deleted.Id, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Deleted, 2, clock.UtcNow, ct, f.Owner))!;
        var originalCards = new[] { (await work.FindCardAsync(card.Id, ct))!, archived, deleted };
        var list = (await work.FindListAsync(f.List, ct))!;
        var beforeAudit = concrete.AuditSnapshot(f.Organization).ToArray();
        var notifications = app.Services.GetRequiredService<IWorkNotificationStore>();
        var beforeNotifications = (await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct)).ToArray();
        var reminderStore = app.Services.GetRequiredService<ICardReminderStore>();
        var sourceEvents = app.Services.GetRequiredService<InMemoryWorkEventStore>();
        var service = app.Services.GetRequiredService<ListBoardMovementService>();
        clock.UtcNow = reminder.TriggerAt!.Value.AddMinutes(1); // Preserve eligible already overdue jobs.
        context.IdempotencyKey = Guid.NewGuid();
        publication.Capture = true; publication.FailBoard = f.Board;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MoveAsync(f.List, f.Owner, f.Board, destination, list.Version, null, "list-publication-test", ct));
        Assert.Equal(list, await work.FindListAsync(f.List, ct));
        foreach (var original in originalCards) Assert.Equal(original, await work.FindCardAsync(original.Id, ct, includeDeleted: true));
        Assert.Equal(beforeAudit, concrete.AuditSnapshot(f.Organization).ToArray());
        Assert.Equal(beforeNotifications, (await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct)).ToArray());
        Assert.Equal(reminder, await reminderStore.FindAsync(f.Organization, f.Recipient, card.Id, ct));
        Assert.Equal(archivedReminder, await reminderStore.FindAsync(f.Organization, f.Recipient, archived.Id, ct));
        Assert.NotEmpty(publication.Sources);
        foreach (var attempted in publication.Sources) Assert.False(sourceEvents.ContainsExact(attempted));
        publication.FailBoard = null; publication.Sources.Clear();
        var success = await service.MoveAsync(f.List, f.Owner, f.Board, destination, list.Version, null, "list-publication-test", ct);
        Assert.True(success.Succeeded);
        var moves = publication.Sources.Where(e => e.EventType is "CARD_MOVED" or "LIST_MOVED").ToArray();
        Assert.Equal(8, moves.Length); Assert.Equal(8, moves.Select(e => e.EventId).Distinct().Count());
        foreach (var id in originalCards.Select(c => c.Id).Append(f.List))
        {
            var pair = moves.Where(e => e.EntityId == id).ToArray(); Assert.Equal(2, pair.Length);
            Assert.Contains(pair, e => e.BoardId == f.Board); Assert.Contains(pair, e => e.BoardId == destination);
            foreach (var persisted in pair) Assert.True(sourceEvents.ContainsExact(persisted));
        }
        var sync = app.Services.GetRequiredService<WorkSynchronizationService>();
        var sourcePage = await sync.ReadAsync(f.Board, f.Recipient, 0, cancellationToken: ct);
        Assert.True(sourcePage.Succeeded);
        foreach (var moved in moves.Where(e => e.BoardId == f.Board))
        {
            var invalidation = Assert.Single(sourcePage.Value!.Events, e => e.EventId == moved.EventId);
            Assert.Equal("BOARD_INVALIDATED", invalidation.EventType);
            Assert.Equal(f.Board, invalidation.EntityId); Assert.Null(invalidation.ActorId);
        }
        var destinationPage = await sync.ReadAsync(destination, f.Owner, 0, cancellationToken: ct);
        Assert.True(destinationPage.Succeeded);
        foreach (var moved in moves.Where(e => e.BoardId == destination))
        {
            var delivered = Assert.Single(destinationPage.Value!.Events, e => e.EventId == moved.EventId);
            if (moved.EntityId == archived.Id || moved.EntityId == deleted.Id)
            { Assert.Equal("BOARD_INVALIDATED", delivered.EventType); Assert.Equal(destination, delivered.EntityId); }
            else { Assert.Equal(moved.EventType, delivered.EventType); Assert.Equal(moved.EntityId, delivered.EntityId); }
        }
        if (!eligible) Assert.Equal("board_not_found", (await sync.ReadAsync(destination, f.Recipient, 0, cancellationToken: ct)).ErrorCode);
        var committedAudit = concrete.AuditSnapshot(f.Organization).ToArray();
        Assert.Equal(3, committedAudit.Count(a => a.EventType == "CARD_MOVED"));
        Assert.Single(committedAudit, a => a.EventType == "LIST_MOVED");
        var watchNotices = (await notifications.ListCardNotificationsAsync(f.Organization, f.Recipient, cancellationToken: ct)).Where(n => n.NotificationType == "CARD_MOVED").ToArray();
        if (eligible) { var notice = Assert.Single(watchNotices); Assert.Equal(destination, notice.BoardId); Assert.Equal(card.Id, notice.CardId); }
        else Assert.Empty(watchNotices);
        var afterReminder = (await reminderStore.FindAsync(f.Organization, f.Recipient, card.Id, ct))!;
        if (eligible) Assert.Equal(reminder, afterReminder);
        else { Assert.Equal("SUSPENDED", afterReminder.Status); Assert.Equal(reminder.Generation + 1, afterReminder.Generation); }
        var afterArchivedReminder = (await reminderStore.FindAsync(f.Organization, f.Recipient, archived.Id, ct))!;
        Assert.Equal("SUSPENDED", afterArchivedReminder.Status);
        Assert.Equal(archivedReminder.Generation + 1, afterArchivedReminder.Generation);
        var sourcesBeforeReplay = publication.Sources.ToArray();
        Assert.Equal(success, await service.MoveAsync(f.List, f.Owner, f.Board, destination, list.Version, null, "list-publication-test", ct));
        Assert.Equal(sourcesBeforeReplay, publication.Sources.ToArray());
        Assert.Equal(committedAudit, concrete.AuditSnapshot(f.Organization).ToArray());
        Assert.Equal(afterReminder, await reminderStore.FindAsync(f.Organization, f.Recipient, card.Id, ct));
    }

    [Fact]
    public async Task PRD_07_List_publication_covers_every_Card_in_a_100000_archived_graph()
    {
        var ct = TestContext.Current.CancellationToken;
        var context = new ListMovementCommandContext { IdempotencyKey = null };
        await using var app = new ApiFactory(configureServices: services => services.AddSingleton<IWorkCommandContext>(context));
        using var owner = app.CreateClient(); await RegisterAndLogin(owner);
        var source = await TelemetryBoard(owner, ct); var work = app.Services.GetRequiredService<IWorkManagementStore>();
        var organization = (await work.FindBoardAsync(source, ct))!.OrganizationId;
        var actor = (await owner.GetFromJsonAsync<JsonElement>("/me", ct)).GetProperty("id").GetGuid();
        using var created = await Mutate(owner, HttpMethod.Post, "/boards", new { organizationId = organization, name = "Large publication target", visibility = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var destination = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;
        var list = await work.CreateListAsync(source, Guid.NewGuid(), "Large publication", RankToken.Initial(), now, ct);
        var ids = new HashSet<Guid>();
        for (var n = 0; n < 100_000; n++)
        {
            var card = await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Archived publication subject", null, RankToken.Initial(), now, ct);
            ids.Add(card.Id);
            Assert.NotNull(await work.SetCardLifecycleAsync(card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 1, now, ct));
        }
        context.IdempotencyKey = Guid.NewGuid();
        var service = app.Services.GetRequiredService<ListBoardMovementService>();
        var result = await service.MoveAsync(list.Id, actor, source, destination, 1, null, "large-list-publication-test", ct);
        Assert.True(result.Succeeded);
        var audit = app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(organization);
        Assert.Equal(100_000, audit.Count(a => a.EventType == "CARD_MOVED"));
        Assert.True(ids.SetEquals(audit.Where(a => a.EventType == "CARD_MOVED").Select(a => a.EntityId)));
        var scope = app.Services.GetRequiredService<DemoWorkTransactionScope>();
        var events = app.Services.GetRequiredService<InMemoryWorkEventStore>();
        using (scope.Enter(organization))
        {
            var activity = events.ActivitySources(organization);
            foreach (var board in new[] { source, destination })
            {
                var moved = activity.Where(e => e.BoardId == board && e.EventType == "CARD_MOVED").ToArray();
                Assert.Equal(100_000, moved.Length); Assert.True(ids.SetEquals(moved.Select(e => e.EntityId)));
                Assert.All(moved, e => Assert.Equal(3, e.Version));
                Assert.Single(activity, e => e.BoardId == board && e.EventType == "LIST_MOVED" && e.EntityId == list.Id);
            }
        }
        Assert.Equal(result, await service.MoveAsync(list.Id, actor, source, destination, 1, null, "large-list-publication-test", ct));
        Assert.Equal(audit.Count, app.Services.GetRequiredService<InMemoryWorkManagementStore>().AuditSnapshot(organization).Count);
    }
}
