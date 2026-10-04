using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

// Internal discovery/revalidation plan. It is not an authorized response:
// consumers must hold its sorted source/current Board gates, re-resolve after
// waits, and verify the current account/session before disclosing a page/cursor.
public sealed record ActivitySourceScope(Guid EventId, Guid OrganizationId, Guid SourceBoardId,
    Guid CurrentBoardId, string TargetType, Guid TargetId, Guid? ParentListId, Guid? PrivateOwnerId);

public sealed class ActivitySourceScopeResolver(IWorkManagementStore work, IOrganizationStore organizations,
    IWorkBoardAuthorization boards, IActivityPrivateTargetStore privateTargets)
{
    public async Task<ActivitySourceScope?> ResolveAsync(ActivityEventSource source, Guid viewer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (viewer == Guid.Empty || source.EventId == Guid.Empty || source.OrganizationId == Guid.Empty ||
            source.BoardId == Guid.Empty || source.ActorId == Guid.Empty || source.EntityId == Guid.Empty || source.Version < 1)
            return null;
        if ((source.EntityType == "WatchSubscription") != (source.EventType is "WATCH_CREATED" or "WATCH_REMOVED") ||
            (source.EntityType == "Reminder") != (source.EventType is "REMINDER_SCHEDULED" or "REMINDER_CANCELLED" or "REMINDER_FIRED"))
            return null;
        var membership = await organizations.FindMembershipAsync(source.OrganizationId, viewer, ct);
        if (membership is not { Active: true } ||
            await organizations.FindOrganizationAsync(source.OrganizationId, ct) is not { Status: OrganizationStatus.Active or OrganizationStatus.Archived })
            return null;
        var original = await ReadBoard(source.OrganizationId, source.BoardId, viewer, ct);
        if (original is null) return null;
        var type = source.EntityType; var id = source.EntityId; Guid? owner = null;
        if (type is "WatchSubscription" or "Reminder")
        {
            var target = await privateTargets.FindAsync(source.OrganizationId, source.EventId, ct);
            // Missing private references never become ordinary shared events.
            if (target is null || target.OwnerId != viewer || target.EntityId == Guid.Empty) return null;
            owner = target.OwnerId; id = target.EntityId;
            type = target.EntityType switch { "BOARD" => "Board", "LIST" => "List", "CARD" => "Card", _ => "" };
            if (source.EntityType == "Reminder" && type != "Card") return null;
        }
        Guid currentBoard; Guid? parentList = null; var deleted = false;
        switch (type)
        {
            case "Board":
                if (owner is null && id != source.BoardId) return null;
                currentBoard = id;
                break;
            case "List":
                var list = await work.FindListAsync(id, ct, includeDeleted: true);
                if (list is null || list.OrganizationId != source.OrganizationId) return null;
                currentBoard = list.BoardId; parentList = list.Id;
                deleted = list.LifecycleState == WorkItemLifecycleState.Deleted;
                break;
            case "Card":
                var card = await work.FindCardAsync(id, ct, includeDeleted: true);
                if (card is null || card.OrganizationId != source.OrganizationId) return null;
                var parent = await work.FindListAsync(card.ListId, ct, includeDeleted: true);
                if (parent is null || parent.OrganizationId != source.OrganizationId || parent.BoardId != card.BoardId) return null;
                currentBoard = card.BoardId; parentList = parent.Id;
                deleted = card.LifecycleState == WorkItemLifecycleState.Deleted || parent.LifecycleState == WorkItemLifecycleState.Deleted;
                break;
            case "Label":
                var label = await work.FindLabelAsync(id, ct, includeDeleted: true);
                if (label is null || label.OrganizationId != source.OrganizationId) return null;
                currentBoard = label.BoardId; deleted = label.Deleted;
                break;
            default:
                return null;
        }
        var current = currentBoard == source.BoardId ? original : await ReadBoard(source.OrganizationId, currentBoard, viewer, ct);
        if (current is null) return null;
        // Archive remains read-only, as on existing comment/attachment reads.
        // Deleted entities yield only body-free history for current administrators.
        if (deleted && membership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin) &&
            await work.FindBoardMemberAsync(currentBoard, viewer, ct) is not { Active: true, Role: BoardRole.Admin })
            return null;
        return new(source.EventId, source.OrganizationId, source.BoardId, currentBoard, type, id, parentList, owner);
    }

    private async Task<BoardSyncScope?> ReadBoard(Guid organizationId, Guid boardId, Guid viewer, CancellationToken ct)
    {
        var scope = await boards.GetSyncScopeAsync(boardId, viewer, ct);
        return scope.Succeeded && scope.Value is { Access.CanView: true } value &&
            value.Board.OrganizationId == organizationId && value.Board.Id == boardId && value.Board.LifecycleState != BoardLifecycleState.Deleted
            ? value : null;
    }
}
