using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

internal static class AttachmentAdmission
{
    public static async Task<bool> CheckAsync(IWorkManagementStore work, IOrganizationStore organizations,
        IWorkBoardAuthorization boards, CardRecord hint, Guid actor, bool editing, CancellationToken ct)
    {
        if (!(editing ? await work.AcquireCommandScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct)
            : await work.AcquireBoardReadScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct))) return false;
        // Internal file/URL metadata requires current Organization membership;
        // public and Owner Portal projections need separate explicit admission.
        if ((await organizations.FindMembershipAsync(hint.OrganizationId, actor, ct)) is not { Active: true }) return false;
        var scope = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
        if (scope.Value is null || !scope.Value.Access.CanView || scope.Value.Board.OrganizationId != hint.OrganizationId
            || editing && (!scope.Value.Access.CanEdit || scope.Value.Board.LifecycleState != BoardLifecycleState.Active)) return false;
        var card = await work.FindCardAsync(hint.Id, ct);
        if (card is null || card.OrganizationId != hint.OrganizationId || card.BoardId != hint.BoardId || card.ListId != hint.ListId
            || editing && card.LifecycleState != WorkItemLifecycleState.Active) return false;
        var list = await work.FindListAsync(card.ListId, ct);
        return list is not null && list.OrganizationId == hint.OrganizationId && list.BoardId == hint.BoardId
            && (!editing || list.LifecycleState == WorkItemLifecycleState.Active);
    }
}
