using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

public sealed record DeleteChecklistItemInput([property: JsonRequired] bool Confirmed, long CardVersion, long ChecklistVersion, long Version);

public sealed partial class ChecklistService
{
    private async Task<bool> AdmitDeletion(CardRecord hint, Guid actor, CancellationToken ct)
    {
        if (!await Admit(hint, actor, true, ct)) return false;
        var scope = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
        return scope.Value?.Access.CanAdminister == true;
    }
    public async Task<WorkOperation<ChecklistItemChange>> DeleteItemAsync(Guid cardId, Guid checklistId, Guid itemId, Guid actor, DeleteChecklistItemInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistItemChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistItemDelete", itemId, new { cardId, checklistId, input }, "checklist_item_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId &&
                receipt.CardId == cardId && receipt.Checklist.Id == checklistId && receipt.Checklist.CardId == cardId &&
                receipt.Checklist.OrganizationId == hint.OrganizationId && receipt.Item.Id == itemId && receipt.Item.ChecklistId == checklistId &&
                receipt.Item.OrganizationId == hint.OrganizationId && receipt.Item.DeletedAt is not null)
                && await AdmitDeletion(hint, actor, ct) && await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct) is not null
                && await checklists.FindItemAsync(hint.OrganizationId, checklistId, itemId, ct, true) is not null,
            async () =>
            {
                if (!input.Confirmed) return WorkOperation<ChecklistItemChange>.Failure("delete_confirmation_required");
                if (input.CardVersion < 1 || input.ChecklistVersion < 1 || input.Version < 1) return WorkOperation<ChecklistItemChange>.Failure("invalid_checklist_version");
                var current = await work.FindCardAsync(cardId, ct);
                var child = await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct);
                var item = await checklists.FindItemAsync(hint.OrganizationId, checklistId, itemId, ct, true);
                if (current is null || child is null || item is null) return WorkOperation<ChecklistItemChange>.Failure("checklist_item_not_found");
                if (current.Version != input.CardVersion || child.Version != input.ChecklistVersion || item.Version != input.Version)
                    return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                if (item.DeletedAt is not null)
                {
                    if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                    return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, child, item, false));
                }
                var now = clock.UtcNow;
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, now, ct);
                if (updated is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var updatedChild = await checklists.RenameAsync(hint.OrganizationId, cardId, checklistId, child.Title, child.Version, now, ct);
                if (updatedChild is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var deleted = await checklists.DeleteItemAsync(hint.OrganizationId, checklistId, itemId, item.Version, now, ct);
                if (deleted is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "CHECKLIST_ITEM_DELETED", "ChecklistItem", itemId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CHECKLIST_ITEM_DELETED",
                    "Card", cardId, updated.Version, correlationId, now), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, updatedChild, deleted, true));
            }, ct);
    }
}
