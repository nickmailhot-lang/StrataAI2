using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record ArchivedCardDetail(Guid OrganizationId, Guid BoardId, Guid CardId,
    string Title, string? Description, long Version);

// The active canvas deliberately excludes archived Cards and Lists. This
// internal read admits their details independently, without granting edits.
public sealed class ArchivedCardDetailService(IWorkManagementStore work, IOrganizationStore organizations,
    IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions)
{
    public async Task<WorkOperation<ArchivedCardDetail>> ReadAsync(Guid boardId, Guid cardId, Guid actor, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null || hint.BoardId != boardId || actor == Guid.Empty)
            return WorkOperation<ArchivedCardDetail>.Failure("card_not_found");
        async Task<bool> Admit()
            => await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, false, ct);
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found", Admit, async () =>
        {
            var card = await work.FindCardAsync(cardId, ct);
            var list = card is null ? null : await work.FindListAsync(card.ListId, ct);
            if (card is null || list is null || card.OrganizationId != hint.OrganizationId || card.BoardId != boardId
                || list.OrganizationId != hint.OrganizationId || list.BoardId != boardId
                || card.LifecycleState == WorkItemLifecycleState.Deleted || list.LifecycleState == WorkItemLifecycleState.Deleted
                || card.LifecycleState != WorkItemLifecycleState.Archived && list.LifecycleState != WorkItemLifecycleState.Archived
                || !await Admit())
                return WorkOperation<ArchivedCardDetail>.Failure("card_not_found");
            return WorkOperation<ArchivedCardDetail>.Success(new(hint.OrganizationId, boardId, card.Id,
                card.Title, card.Description, card.Version));
        }, ct);
    }
}
