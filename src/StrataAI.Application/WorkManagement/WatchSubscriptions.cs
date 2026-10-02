using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record WatchSubscription(Guid Id, Guid OrganizationId, Guid UserId, string EntityType,
    Guid EntityId, bool Watching, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);
public sealed record WatchState(Guid OrganizationId, Guid BoardId, Guid UserId, string EntityType,
    Guid EntityId, bool Watching, long Version, Guid? SubscriptionId, DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt, bool Changed, bool CanChange);

public interface IWatchSubscriptionStore
{
    Task<WatchSubscription?> FindAsync(Guid organizationId, Guid userId, string entityType, Guid entityId, CancellationToken ct);
    Task<WatchSubscription?> SetAsync(Guid organizationId, Guid userId, string entityType, Guid entityId,
        bool watching, long expectedVersion, DateTimeOffset now, CancellationToken ct);
}

// Personal watch intent shares the entity's command gate with movement/activity.
// No subscription response is a substitute for fresh entity/recipient admission.
public sealed class WatchSubscriptionService(IWatchSubscriptionStore subscriptions, IWorkManagementStore work,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions,
    IWorkEventStore events, ICommandActorAuthorization actors, IWorkCommandContext context, IClock clock)
{
    private sealed record Scope(Guid OrganizationId, Guid BoardId, Guid? ListId);

    public Task<WorkOperation<WatchState>> GetAsync(string type, Guid entityId, Guid userId, CancellationToken ct = default) =>
        Execute(type, entityId, userId, null, 0, "", ct);
    public Task<WorkOperation<WatchState>> SetAsync(string type, Guid entityId, Guid userId, bool watching,
        long expectedVersion, string correlationId, CancellationToken ct = default) =>
        Execute(type, entityId, userId, watching, expectedVersion, correlationId, ct);

    private async Task<WorkOperation<WatchState>> Execute(string type, Guid entityId, Guid userId,
        bool? watching, long expectedVersion, string correlationId, CancellationToken ct)
    {
        if (entityId == Guid.Empty || userId == Guid.Empty || type is not ("CARD" or "LIST" or "BOARD"))
            return WorkOperation<WatchState>.Failure("watch_not_found");
        var hint = await Resolve(type, entityId, ct);
        if (hint is null) return WorkOperation<WatchState>.Failure("watch_not_found");
        var canChange = false;
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(userId, watching is null ? null : context.IdempotencyKey, "WatchSubscription", entityId,
                new { type, watching, expectedVersion }, "watch_not_found"), async receipt =>
            {
                if (receipt is not null && (receipt.OrganizationId != hint.OrganizationId || receipt.BoardId != hint.BoardId ||
                    receipt.UserId != userId || receipt.EntityType != type || receipt.EntityId != entityId)) return false;
                var locked = watching is null
                    ? await work.AcquireBoardReadScopeAsync(hint.OrganizationId, userId, hint.BoardId, ct)
                    : await work.AcquireCommandScopeAsync(hint.OrganizationId, userId, hint.BoardId, ct);
                if (!locked || await organizations.FindMembershipAsync(hint.OrganizationId, userId, ct) is not { Active: true }) return false;
                var organization = await organizations.FindOrganizationAsync(hint.OrganizationId, ct);
                if (organization is null || (watching is not null ? organization.Status != OrganizationStatus.Active :
                    organization.Status is not (OrganizationStatus.Active or OrganizationStatus.Archived))) return false;
                canChange = organization.Status == OrganizationStatus.Active;
                var scope = await Resolve(type, entityId, ct);
                if (scope != hint) return false;
                var view = await boards.GetSyncScopeAsync(hint.BoardId, userId, ct);
                return view.Value is { Access.CanView: true, Board.LifecycleState: BoardLifecycleState.Active }
                    && view.Value.Board.OrganizationId == hint.OrganizationId;
            }, async () =>
            {
                var current = await subscriptions.FindAsync(hint.OrganizationId, userId, type, entityId, ct);
                if (watching is not null)
                {
                    if (expectedVersion < 0) return WorkOperation<WatchState>.Failure("invalid_watch_version");
                    if ((current?.Version ?? 0) != expectedVersion) return WorkOperation<WatchState>.Failure("version_conflict");
                    if ((current?.Watching ?? false) != watching.Value)
                    {
                        current = await subscriptions.SetAsync(hint.OrganizationId, userId, type, entityId,
                            watching.Value, expectedVersion, clock.UtcNow, ct);
                        if (current is null) return WorkOperation<WatchState>.Failure("version_conflict");
                        var eventType = watching.Value ? "WATCH_CREATED" : "WATCH_REMOVED";
                        await work.AppendAuditAsync(hint.OrganizationId, userId, eventType, "WatchSubscription", current.Id, correlationId, ct);
                        await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, userId,
                            eventType, "WatchSubscription", current.Id, current.Version, correlationId, clock.UtcNow), ct);
                        if (!await actors.VerifyAsync(userId, ct)) return WorkOperation<WatchState>.Failure("session_unavailable");
                        return WorkOperation<WatchState>.Success(State(hint, userId, type, entityId, current, true, canChange));
                    }
                }
                if (!await actors.VerifyAsync(userId, ct)) return WorkOperation<WatchState>.Failure("session_unavailable");
                return WorkOperation<WatchState>.Success(State(hint, userId, type, entityId, current, false, canChange));
            }, ct);
    }

    private static WatchState State(Scope scope, Guid user, string type, Guid id, WatchSubscription? row, bool changed, bool canChange) =>
        new(scope.OrganizationId, scope.BoardId, user, type, id, row?.Watching ?? false, row?.Version ?? 0,
            row?.Id, row?.CreatedAt, row?.UpdatedAt, changed, canChange);

    private async Task<Scope?> Resolve(string type, Guid entityId, CancellationToken ct)
    {
        if (type == "BOARD")
        {
            var board = await work.FindBoardAsync(entityId, ct);
            return board is { LifecycleState: BoardLifecycleState.Active } ? new(board.OrganizationId, board.Id, null) : null;
        }
        if (type == "LIST")
        {
            var list = await work.FindListAsync(entityId, ct);
            return list is { LifecycleState: WorkItemLifecycleState.Active } ? new(list.OrganizationId, list.BoardId, list.Id) : null;
        }
        var card = await work.FindCardAsync(entityId, ct);
        if (card is not { LifecycleState: WorkItemLifecycleState.Active }) return null;
        var parent = await work.FindListAsync(card.ListId, ct);
        return parent is { LifecycleState: WorkItemLifecycleState.Active } && parent.BoardId == card.BoardId &&
            parent.OrganizationId == card.OrganizationId ? new(card.OrganizationId, card.BoardId, card.ListId) : null;
    }
}
