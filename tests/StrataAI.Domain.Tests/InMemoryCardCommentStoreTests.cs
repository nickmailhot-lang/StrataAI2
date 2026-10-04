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

public sealed class InMemoryCardCommentStoreTests
{
    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("cancel")]
    [InlineData("session")]
    public async Task PRD_15_DemoMentionSnapshotsRetainImmutableHistoryAndRollBackWithTheirComment(string mode)
    {
        var ct = TestContext.Current.CancellationToken; var actor = new Actor(); using var services = Demo(actor);
        var parent = await Parent(services, ct); var comments = services.GetRequiredService<ICardCommentStore>();
        var snapshots = services.GetRequiredService<ICommentMentionSnapshotStore>(); var unit = services.GetRequiredService<IWorkManagementUnitOfWork>();
        var id = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<InvalidOperationException>(() => snapshots.FindSnapshotAsync(parent.Org, parent.Card.Id, id, 1, ct));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task<WorkOperation<CommentMentionSnapshot>> Attempt()
            => await unit.ExecuteReadAsync(parent.Org, parent.User, "fixture_denied", () => Task.FromResult(true), async () =>
            {
                var comment = await comments.CreateAsync(id, parent.Org, parent.Card.Id, parent.User, "Private comment body", at, ct);
                var input = new List<Guid> { parent.User }; var snapshot = new CommentMentionSnapshot(parent.Org, parent.Card.Id, id, 1, comment.UpdatedAt, input);
                input.Clear(); await snapshots.AppendSnapshotAsync(snapshot, ct);
                if (mode == "exception") throw new InvalidOperationException("fixture refusal");
                if (mode == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
                if (mode == "session") { actor.Allowed = false; return WorkOperation<CommentMentionSnapshot>.Success(snapshot); }
                return WorkOperation<CommentMentionSnapshot>.Failure("fixture_refused");
            }, ct);
        if (mode == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => Attempt());
        else if (mode == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Attempt());
        else Assert.Equal(mode == "session" ? "session_unavailable" : "fixture_refused", (await Attempt()).ErrorCode);
        actor.Allowed = true;
        Assert.Null(await Scoped(services, parent.Org, () => comments.FindAsync(parent.Org, parent.Card.Id, id, ct), ct));
        Assert.Null(await Scoped(services, parent.Org, () => snapshots.FindSnapshotAsync(parent.Org, parent.Card.Id, id, 1, ct), ct));
        var initial = await Scoped(services, parent.Org, async () =>
        {
            var comment = await comments.CreateAsync(id, parent.Org, parent.Card.Id, parent.User, "Private comment body", at, ct);
            var snapshot = new CommentMentionSnapshot(parent.Org, parent.Card.Id, id, 1, comment.UpdatedAt, [parent.User]);
            await snapshots.AppendSnapshotAsync(snapshot, ct); await snapshots.AppendSnapshotAsync(snapshot, ct); return snapshot;
        }, ct);
        Assert.Single(initial.Recipients); Assert.DoesNotContain("Private comment body", System.Text.Json.JsonSerializer.Serialize(initial));
        await Scoped(services, parent.Org, async () =>
        {
            var edited = await comments.EditAsync(parent.Org, parent.Card.Id, id, parent.User, 1, "Edited", at.AddSeconds(1), ct);
            await snapshots.AppendSnapshotAsync(new(parent.Org, parent.Card.Id, id, 2, edited!.UpdatedAt, []), ct);
            await snapshots.AppendSnapshotAsync(initial, ct); // Exact historical retry has no write.
            Assert.Single((await snapshots.FindSnapshotAsync(parent.Org, parent.Card.Id, id, 1, ct))!.Recipients);
            Assert.Empty((await snapshots.FindSnapshotAsync(parent.Org, parent.Card.Id, id, 2, ct))!.Recipients);
            await Assert.ThrowsAsync<InvalidOperationException>(() => snapshots.AppendSnapshotAsync(new(parent.Org, parent.Card.Id, id, 1, initial.CreatedAt, []), ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => snapshots.AppendSnapshotAsync(new(parent.Org, parent.Card.Id, id, 3, edited.UpdatedAt, []), ct));
            Assert.Null(await snapshots.FindSnapshotAsync(parent.Org, Guid.NewGuid(), id, 1, ct));
            return true;
        }, ct);
        Assert.Throws<ArgumentException>(() => new CommentMentionSnapshot(parent.Org, parent.Card.Id, id, 2, initial.CreatedAt, [Guid.Empty]));
        Assert.Throws<ArgumentException>(() => new CommentMentionSnapshot(parent.Org, parent.Card.Id, id, 2, initial.CreatedAt, [parent.User, parent.User]));
        Assert.Throws<ArgumentException>(() => new CommentMentionSnapshot(parent.Org, parent.Card.Id, id, 2, initial.CreatedAt, Enumerable.Range(0,21).Select(_ => Guid.NewGuid()).ToArray()));
    }
    private sealed class Actor : ICommandActorAuthorization
    { public bool Allowed = true; public Task<bool> VerifyAsync(Guid actorId, CancellationToken ct = default) => Task.FromResult(Allowed); }
    private sealed class CommandContext : IWorkCommandContext
    { public Guid? IdempotencyKey { get; set; } }
    private static ServiceProvider Demo(Actor actor)
    {
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock, SystemClock>(); services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        services.AddSingleton<ICommandActorAuthorization>(actor);
        services.AddSingleton<CommandContext>(); services.AddSingleton<IWorkCommandContext>(p => p.GetRequiredService<CommandContext>());
        return services.BuildServiceProvider();
    }
    private static async Task<(Guid Org,Guid User,CardRecord Card)> Parent(ServiceProvider services, CancellationToken ct)
    {
        var at = DateTimeOffset.UtcNow; var org = Guid.NewGuid(); var user = Guid.NewGuid();
        await services.GetRequiredService<IOrganizationStore>().CreateOrganizationAsync(user, org, "Comments", null, at, ct);
        var work = services.GetRequiredService<IWorkManagementStore>();
        var board = await work.CreateBoardAsync(org, user, Guid.NewGuid(), "Board", null, BoardVisibility.Private, "COLOR", null, at, ct);
        var list = await work.CreateListAsync(board.Id, Guid.NewGuid(), "List", null, at, ct);
        return (org, user, await work.CreateCardAsync(list.Id, Guid.NewGuid(), "Card", null, null, at, ct));
    }
    private static async Task<T> Scoped<T>(ServiceProvider services, Guid org, Func<Task<T>> action, CancellationToken ct)
    {
        var result = await services.GetRequiredService<IWorkManagementUnitOfWork>().ExecuteReadAsync(org, null, "fixture_denied",
            () => Task.FromResult(true), async () => WorkOperation<T>.Success(await action()), ct);
        Assert.True(result.Succeeded); return result.Value!;
    }
    [Fact]
    public async Task PRD_15_TC_01_DemoCommentScopePagingAuthorCASAndRedactedHistoryMatchProductionContract()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(new()); var parent = await Parent(services, ct);
        var store = services.GetRequiredService<ICardCommentStore>(); var at = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(parent.Org, parent.Card.Id, Guid.NewGuid(), ct));
        var rows = await Scoped(services, parent.Org, async () =>
        {
            var values = new List<CardCommentRecord>();
            for (var index = 0; index < 63; index++) values.Add(await store.CreateAsync(Guid.NewGuid(), parent.Org, parent.Card.Id, parent.User, "  Plain\r\n🙂  ", at, ct));
            return values;
        }, ct);
        var first = await Scoped(services, parent.Org, () => store.ListAsync(parent.Org, parent.Card.Id, null, null, ct), ct);
        Assert.Equal(51, first.Count); var anchor = first[49];
        var tail = await Scoped(services, parent.Org, () => store.ListAsync(parent.Org, parent.Card.Id, anchor.CreatedAt, anchor.Id, ct), ct);
        Assert.Equal(13, tail.Count); Assert.Equal(63, first.Take(50).Concat(tail).Select(x => x.Id).Distinct().Count());
        var row = rows[0]; Assert.Equal("Plain\n🙂", row.Content);
        Assert.Null(await Scoped(services, parent.Org, () => store.EditAsync(parent.Org, parent.Card.Id, row.Id, Guid.NewGuid(), 1, "Foreign", at.AddSeconds(1), ct), ct));
        var edited = await Scoped(services, parent.Org, () => store.EditAsync(parent.Org, parent.Card.Id, row.Id, parent.User, 1, "Edited", at.AddSeconds(1), ct), ct);
        Assert.Equal(2, edited!.Version);
        Assert.Null(await Scoped(services, parent.Org, () => store.DeleteAsync(parent.Org, parent.Card.Id, row.Id, parent.User, 1, at.AddSeconds(2), ct), ct));
        var deleted = await Scoped(services, parent.Org, () => store.DeleteAsync(parent.Org, parent.Card.Id, row.Id, parent.User, 2, at.AddSeconds(2), ct), ct);
        Assert.Null(deleted!.Content); Assert.Equal(3, deleted.Version); Assert.Equal(edited.EditedAt, deleted.EditedAt); Assert.Equal(parent.User, deleted.DeletedBy);
        Assert.Null(await Scoped(services, parent.Org, () => store.EditAsync(parent.Org, parent.Card.Id, row.Id, parent.User, 3, "Revive", at.AddSeconds(3), ct), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(parent.Org, parent.Card.Id, row.Id, ct));
    }
    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("session")]
    public async Task PRD_15_TC_06_FailedDemoCommandRollsBackCommentCardEventAndNotificationThenOriginalKeyRecovers(string mode)
    {
        var ct = TestContext.Current.CancellationToken; var actor = new Actor(); using var services = Demo(actor);
        var parent = await Parent(services, ct); var comments = services.GetRequiredService<ICardCommentStore>();
        var work = services.GetRequiredService<IWorkManagementStore>(); var events = services.GetRequiredService<IWorkEventStore>();
        var notifications = services.GetRequiredService<IWorkNotificationStore>(); var unit = services.GetRequiredService<IWorkManagementUnitOfWork>();
        var recipient = Guid.NewGuid(); var id = Guid.NewGuid(); var eventId = Guid.NewGuid(); var attempts = 0; var at = DateTimeOffset.UtcNow.AddMinutes(1);
        var command = WorkCommand.Create(parent.User, Guid.NewGuid(), "COMMENT_FIXTURE", parent.Card.Id, new { id }, "fixture_denied");
        async Task<WorkOperation<CardCommentRecord>> Execute() => await unit.ExecuteAsync(parent.Org, command, _ => Task.FromResult(true), async () =>
        {
            attempts++; var comment = await comments.CreateAsync(id, parent.Org, parent.Card.Id, parent.User, "Private comment", at, ct);
            var updated = await work.UpdateCardAsync(parent.Card.Id, "Updated", null, parent.Card.Version, at, ct); Assert.NotNull(updated);
            // Existing supported assignment event exercises event/notification
            // rollback; it is not a claim that mention production exists yet.
            var change = new WorkEvent(eventId, parent.Org, parent.Card.BoardId, parent.User, "CARD_MEMBER_ADDED", "Card", parent.Card.Id, updated!.Version, "comment-fixture", at);
            await events.AppendAsync(change, ct); await notifications.AppendCardAssignmentAsync(change, recipient, ct);
            if (attempts == 1)
            {
                if (mode == "exception") throw new InvalidOperationException("fixture failure");
                if (mode == "session") actor.Allowed = false;
                else return WorkOperation<CardCommentRecord>.Failure("fixture_refused");
            }
            return WorkOperation<CardCommentRecord>.Success(comment);
        }, ct);
        if (mode == "exception") await Assert.ThrowsAsync<InvalidOperationException>(() => Execute());
        else Assert.Equal(mode == "session" ? "session_unavailable" : "fixture_refused", (await Execute()).ErrorCode);
        actor.Allowed = true;
        Assert.Null(await Scoped(services, parent.Org, () => comments.FindAsync(parent.Org, parent.Card.Id, id, ct), ct));
        Assert.Equal(parent.Card, await work.FindCardAsync(parent.Card.Id, ct));
        Assert.Empty(await notifications.ListCardNotificationsAsync(parent.Org, recipient, cancellationToken: ct));
        Assert.Empty((await services.GetRequiredService<IWorkEventReader>().ReadAsync(parent.Org, parent.Card.BoardId, 0, 50, ct)).Events);
        var success = await Execute(); Assert.True(success.Succeeded); Assert.Equal(2, attempts);
        Assert.Equal(success.Value, (await Execute()).Value); Assert.Equal(2, attempts);
        Assert.Single(await notifications.ListCardNotificationsAsync(parent.Org, recipient, cancellationToken: ct));
        Assert.Single((await services.GetRequiredService<IWorkEventReader>().ReadAsync(parent.Org, parent.Card.BoardId, 0, 50, ct)).Events);
    }

    [Fact]
    public async Task PRD_15_TC_01_07_ApplicationCommentCommandsConsumeBothRevisionsAndNeverReplayRedactedBody()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(new()); var parent = await Parent(services, ct);
        var service = services.GetRequiredService<CardCommentService>(); var context = services.GetRequiredService<CommandContext>();
        var reader = services.GetRequiredService<IWorkEventReader>();
        var key = Guid.NewGuid(); context.IdempotencyKey = key; var input = new CreateCardCommentInput("  First\r\n🙂  ", 1);
        var created = await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-test", ct);
        Assert.True(created.Succeeded); var row = created.Value!; Assert.Equal(2, row.CardVersion); Assert.Equal(1, row.Comment.Version);
        Assert.Equal("First\n🙂", row.Comment.Content);
        Assert.Equal(row, (await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-retry", ct)).Value);
        var retained = await services.GetRequiredService<IWorkManagementUnitOfWork>().ExecuteAsync<CardCommentService.Receipt>(parent.Org,
            WorkCommand.Create(parent.User, key, "COMMENT_ADDED", parent.Card.Id, new { cardId = parent.Card.Id, input }, "fixture_denied"),
            _ => Task.FromResult(true), () => throw new InvalidOperationException("Committed receipt was lost."), ct);
        Assert.True(retained.Succeeded); Assert.Equal(row.Comment.Id, retained.Value!.CommentId);
        var retainedJson = System.Text.Json.JsonSerializer.Serialize(retained);
        Assert.DoesNotContain("First", retainedJson); Assert.DoesNotContain("Content", retainedJson);
        var recoveredReceipt = System.Text.Json.JsonSerializer.Deserialize<WorkOperation<CardCommentService.Receipt>>(retainedJson);
        Assert.Equal(retained, recoveredReceipt);
        context.IdempotencyKey = Guid.NewGuid();
        var noop = await service.EditAsync(parent.Card.Id, row.Comment.Id, parent.User, new("First\n🙂", 2, 1), "comment-test", ct);
        Assert.True(noop.Succeeded); Assert.False(noop.Value!.Changed); Assert.Equal(2, noop.Value.CardVersion);
        context.IdempotencyKey = Guid.NewGuid();
        var edited = await service.EditAsync(parent.Card.Id, row.Comment.Id, parent.User, new("Edited", 2, 1), "comment-test", ct);
        Assert.True(edited.Succeeded); Assert.Equal(3, edited.Value!.CardVersion); Assert.Equal(2, edited.Value.Comment.Version);
        context.IdempotencyKey = Guid.NewGuid();
        var unconfirmed = await service.DeleteAsync(parent.Card.Id, row.Comment.Id, parent.User, new(3, 2, false), "comment-test", ct);
        Assert.Equal("comment_delete_confirmation_required", unconfirmed.ErrorCode);
        var deleted = await service.DeleteAsync(parent.Card.Id, row.Comment.Id, parent.User, new(3, 2, true), "comment-test", ct);
        Assert.True(deleted.Succeeded); Assert.Equal(4, deleted.Value!.CardVersion); Assert.Equal(3, deleted.Value.Comment.Version);
        Assert.Null(deleted.Value.Comment.Content); Assert.Equal(edited.Value.Comment.EditedAt, deleted.Value.Comment.EditedAt);
        context.IdempotencyKey = key;
        Assert.Equal("comment_not_found", (await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-retry", ct)).ErrorCode);
        var page = await service.ListAsync(parent.Card.Id, parent.User, null, ct);
        Assert.True(page.Succeeded); Assert.True(page.Value!.CanComment); Assert.Null(Assert.Single(page.Value.Items).Content);
        var changes = (await reader.ReadAsync(parent.Org, parent.Card.BoardId, 0, 50, ct)).Events;
        Assert.Equal(new[] { "COMMENT_ADDED", "COMMENT_EDITED", "COMMENT_DELETED" }, changes.Select(x => x.Event.EventType));
        Assert.All(changes, x => { Assert.Equal("Card", x.Event.EntityType); Assert.Equal(parent.Card.Id, x.Event.EntityId); });
    }

    [Fact]
    public async Task PRD_15_TC_03_04_ApplicationRefusesInvalidForeignAuthorAndGovernanceWithoutBoardParticipation()
    {
        var ct = TestContext.Current.CancellationToken; using var services = Demo(new()); var parent = await Parent(services, ct);
        var service = services.GetRequiredService<CardCommentService>(); var work = services.GetRequiredService<IWorkManagementStore>();
        var orgs = services.GetRequiredService<IOrganizationStore>(); var context = services.GetRequiredService<CommandContext>();
        context.IdempotencyKey = Guid.NewGuid();
        Assert.Equal("invalid_comment_content", (await service.CreateAsync(parent.Card.Id, parent.User, new(" \0 ", 1), "comment-test", ct)).ErrorCode);
        Assert.Empty((await service.ListAsync(parent.Card.Id, parent.User, null, ct)).Value!.Items);
        var created = (await service.CreateAsync(parent.Card.Id, parent.User, new("Owned", 1), "comment-test", ct)).Value!;
        var governor = Guid.NewGuid(); await orgs.AddOrRestoreMemberAsync(parent.Org, governor, OrganizationRole.Admin, DateTimeOffset.UtcNow, ct);
        context.IdempotencyKey = Guid.NewGuid();
        Assert.Equal("comment_not_found", (await service.CreateAsync(parent.Card.Id, governor, new("Denied", 2), "comment-test", ct)).ErrorCode);
        await work.UpsertBoardMemberAsync(parent.Card.BoardId, governor, BoardRole.Member, DateTimeOffset.UtcNow, ct);
        Assert.Equal("comment_not_found", (await service.EditAsync(parent.Card.Id, created.Comment.Id, governor, new("Foreign", 2, 1), "comment-test", ct)).ErrorCode);
        Assert.Equal("comment_not_found", (await service.DeleteAsync(parent.Card.Id, created.Comment.Id, governor, new(2, 1, true), "comment-test", ct)).ErrorCode);
        Assert.Equal("version_conflict", (await service.EditAsync(parent.Card.Id, created.Comment.Id, parent.User, new("Stale", 1, 1), "comment-test", ct)).ErrorCode);
        Assert.Equal(created.Comment, Assert.Single((await service.ListAsync(parent.Card.Id, parent.User, null, ct)).Value!.Items));
        Assert.Equal(2, (await work.FindCardAsync(parent.Card.Id, ct))!.Version);
        Assert.Equal("invalid_comment_cursor", (await service.ListAsync(parent.Card.Id, parent.User, new string('x', 161), ct)).ErrorCode);
        Assert.False((await service.ListAsync(parent.Card.Id, Guid.Empty, null, ct)).Succeeded);
    }

    [Fact]
    public async Task PRD_15_TC_05_10_CurrentMembershipSessionAndArchivedParentsRefuseFreshCommandsAndReceipts()
    {
        var ct = TestContext.Current.CancellationToken; var actor = new Actor(); using var services = Demo(actor); var parent = await Parent(services, ct);
        var service = services.GetRequiredService<CardCommentService>(); var work = services.GetRequiredService<IWorkManagementStore>();
        var context = services.GetRequiredService<CommandContext>(); context.IdempotencyKey = Guid.NewGuid(); var input = new CreateCardCommentInput("Retained", 1);
        var created = (await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-test", ct)).Value!;
        actor.Allowed = false;
        Assert.Equal("session_unavailable", (await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-test", ct)).ErrorCode);
        actor.Allowed = true;
        await work.SetCardLifecycleAsync(parent.Card.Id, WorkItemLifecycleState.Active, WorkItemLifecycleState.Archived, 2, DateTimeOffset.UtcNow, ct);
        Assert.Equal("comment_not_found", (await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-test", ct)).ErrorCode);
        var archived = (await service.ListAsync(parent.Card.Id, parent.User, null, ct)).Value!;
        Assert.False(archived.CanComment); Assert.Equal(created.Comment, Assert.Single(archived.Items));
        await work.SetCardLifecycleAsync(parent.Card.Id, WorkItemLifecycleState.Archived, WorkItemLifecycleState.Active, 3, DateTimeOffset.UtcNow, ct);
        await work.RemoveBoardMemberAsync(parent.Card.BoardId, parent.User, DateTimeOffset.UtcNow, ct);
        Assert.Equal("comment_not_found", (await service.CreateAsync(parent.Card.Id, parent.User, input, "comment-test", ct)).ErrorCode);
        Assert.False((await service.ListAsync(parent.Card.Id, parent.User, null, ct)).Value!.CanComment);
    }
}
