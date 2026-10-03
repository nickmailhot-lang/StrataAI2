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
    private sealed class Actor : ICommandActorAuthorization
    { public bool Allowed = true; public Task<bool> VerifyAsync(Guid actorId, CancellationToken ct = default) => Task.FromResult(Allowed); }
    private static ServiceProvider Demo(Actor actor)
    {
        var services = new ServiceCollection(); var runtime = new RuntimeDescriptor(RuntimeMode.Demo, "test", "test");
        services.AddSingleton<IClock, SystemClock>(); services.AddStrataAiIdentity(new ConfigurationBuilder().Build(), runtime);
        services.AddStrataAiOrganizations(runtime); services.AddStrataAiWorkManagement(runtime);
        services.AddSingleton<ICommandActorAuthorization>(actor); return services.BuildServiceProvider();
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
}
