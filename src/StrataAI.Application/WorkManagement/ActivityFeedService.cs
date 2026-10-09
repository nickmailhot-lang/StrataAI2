using System.Globalization;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed class ActivityFeedService(IActivityFeedStore feed, ActivitySourceScopeResolver scopes,
    IWorkManagementStore work, IOrganizationStore organizations, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, IActivityCursorCodec cursors)
{
    private sealed record Root(Guid BoardId, Guid? ListId);
    public async Task<WorkOperation<ActivityPage>> ReadAsync(ActivityTargetKind kind, Guid targetId, Guid viewer,
        string? after, CancellationToken ct = default)
    {
        if (viewer == Guid.Empty || targetId == Guid.Empty || !Enum.IsDefined(kind)) return WorkOperation<ActivityPage>.Failure("activity_not_found");
        var organization = kind == ActivityTargetKind.Board ? (await work.FindBoardAsync(targetId, ct))?.OrganizationId
            : (await work.FindCardAsync(targetId, ct, includeDeleted: true))?.OrganizationId;
        if (organization is null) return WorkOperation<ActivityPage>.Failure("activity_not_found");
        var binding = new ActivityCursorBinding(organization.Value, viewer, kind, targetId);
        var prepared = false; var invalid = false; var unavailable = false; Root? plannedRoot = null;
        IReadOnlyList<ActivityEventSource> rows = []; var plans = new List<ActivitySourceScope>();
        async Task<bool> Authorize()
        {
            // Repeated history rows share a target, not an event identity. Reuse
            // target discovery only within this admission pass. Private event
            // references remain independent; nothing survives a Board-gate wait
            // or the transaction's next authorization callback.
            var resolve = scopes.CreateReadPass(viewer);
            if (!await work.AcquireOrganizationReadScopeAsync(binding.OrganizationId, viewer, ct)) return false;
            var root = await ResolveRoot(binding, ct); if (root is null) return false;
            if (!prepared)
            {
                plannedRoot = root; ActivityCursor? position = null;
                invalid = after is not null && !cursors.TryDecode(binding, after, out position);
                if (!invalid)
                {
                    rows = await feed.ReadAsync(binding, position, ct);
                    if (!ValidWindow(rows, binding, position)) unavailable = true;
                    if (!unavailable)
                        foreach (var row in rows)
                        {
                            var plan = await resolve(row, ct);
                            if (plan is null || kind == ActivityTargetKind.Card &&
                                (plan.TargetType != "Card" || plan.TargetId != targetId || plan.CurrentBoardId != root.BoardId || plan.ParentListId != root.ListId))
                            { unavailable = true; break; }
                            plans.Add(plan);
                        }
                }
                // Discover the entire lock set BEFORE holding any Board gate.
                // Holding the current Card Board first could reverse copy/move order.
                var gates = unavailable ? new[] { root.BoardId } : plans.SelectMany(plan => new[] { plan.SourceBoardId, plan.CurrentBoardId }).Append(root.BoardId);
                foreach (var board in gates.Distinct().Order())
                    if (!await work.AcquireBoardReadScopeAsync(binding.OrganizationId, viewer, board, ct)) return false;
                resolve = scopes.CreateReadPass(viewer);
                prepared = true;
            }
            if (await ResolveRoot(binding, ct) != plannedRoot) return false;
            if (!unavailable)
                for (var index = 0; index < plans.Count; index++)
                    if (await resolve(rows[index], ct) != plans[index]) return false;
            return true;
        }
        try
        {
            return await transactions.ExecuteReadAsync(binding.OrganizationId, viewer, "activity_not_found", Authorize, () =>
            {
                if (invalid) return Task.FromResult(WorkOperation<ActivityPage>.Failure("invalid_activity_cursor"));
                if (unavailable) return Task.FromResult(WorkOperation<ActivityPage>.Failure("activity_unavailable"));
                var items = rows.Take(50).Select((row, index) => new ActivityItem(row.EventId, row.OrganizationId, row.BoardId,
                    row.ActorId, row.ActorLabel, row.EventType, row.EntityType, row.EntityId, row.Version.ToString(CultureInfo.InvariantCulture),
                    row.CreatedAt, row.Metadata, plans[index].CurrentBoardId)).ToArray();
                var next = rows.Count > 50 ? cursors.Encode(binding, new(rows[49].CreatedAt, rows[49].EventId)) : null;
                return Task.FromResult(WorkOperation<ActivityPage>.Success(new(binding.OrganizationId, kind, targetId,
                    Array.AsReadOnly(items), next)));
            }, ct);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        { return WorkOperation<ActivityPage>.Failure("activity_unavailable"); }
    }

    private async Task<Root?> ResolveRoot(ActivityCursorBinding binding, CancellationToken ct)
    {
        var membership = await organizations.FindMembershipAsync(binding.OrganizationId, binding.ViewerId, ct);
        if (membership is not { Active: true } || await organizations.FindOrganizationAsync(binding.OrganizationId, ct) is not
            { Status: OrganizationStatus.Active or OrganizationStatus.Archived }) return null;
        Guid boardId; Guid? listId = null; var deleted = false;
        if (binding.Kind == ActivityTargetKind.Board) boardId = binding.TargetId;
        else
        {
            var card = await work.FindCardAsync(binding.TargetId, ct, includeDeleted: true);
            if (card is null || card.OrganizationId != binding.OrganizationId) return null;
            var parent = await work.FindListAsync(card.ListId, ct, includeDeleted: true);
            if (parent is null || parent.OrganizationId != binding.OrganizationId || parent.BoardId != card.BoardId) return null;
            boardId = card.BoardId; listId = parent.Id;
            deleted = card.LifecycleState == WorkItemLifecycleState.Deleted || parent.LifecycleState == WorkItemLifecycleState.Deleted;
        }
        var scope = await boards.GetSyncScopeAsync(boardId, binding.ViewerId, ct);
        if (scope.Value is not { Access.CanView: true } || scope.Value.Board.OrganizationId != binding.OrganizationId ||
            scope.Value.Board.Id != boardId || scope.Value.Board.LifecycleState == BoardLifecycleState.Deleted) return null;
        if (deleted && membership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin) &&
            await work.FindBoardMemberAsync(boardId, binding.ViewerId, ct) is not { Active: true, Role: BoardRole.Admin }) return null;
        return new(boardId, listId);
    }

    private static bool ValidWindow(IReadOnlyList<ActivityEventSource> rows, ActivityCursorBinding binding, ActivityCursor? before)
    {
        if (rows.Count > 51) return false;
        var identities = new HashSet<Guid>(); var previous = before;
        foreach (var row in rows)
        {
            if (row.OrganizationId != binding.OrganizationId || row.BoardId == Guid.Empty || row.EventId == Guid.Empty ||
                row.ActorId == Guid.Empty || row.EntityId == Guid.Empty || row.Version < 1 || !identities.Add(row.EventId) ||
                binding.Kind == ActivityTargetKind.Board && row.BoardId != binding.TargetId || row.Metadata.Count != 0 ||
                string.IsNullOrEmpty(row.ActorLabel) || row.ActorLabel.EnumerateRunes().Count() > 160 || row.ActorLabel.Any(char.IsControl) ||
                string.IsNullOrEmpty(row.EventType) || row.EventType.Length > 80 || row.EventType[0] is < 'A' or > 'Z' ||
                row.EventType.Any(ch => ch is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_') ||
                row.EntityType is not ("Board" or "List" or "Card" or "Label" or "WatchSubscription" or "Reminder")) return false;
            ActivityEventSourceWindow.RequireCursor(row.CreatedAt, row.EventId);
            if (previous is not null && (row.CreatedAt > previous.CreatedAt || row.CreatedAt == previous.CreatedAt &&
                string.CompareOrdinal(row.EventId.ToString("N"), previous.EventId.ToString("N")) >= 0)) return false;
            previous = new(row.CreatedAt, row.EventId);
        }
        return true;
    }
}
