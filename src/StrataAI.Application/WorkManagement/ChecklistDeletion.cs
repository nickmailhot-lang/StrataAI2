using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

public sealed record DeleteChecklistInput([property: JsonRequired] bool Confirmed, long CardVersion, long Version);
public sealed record ChecklistDeletionStoreResult(ChecklistRecord Checklist, long DeletedItems);
public sealed record ChecklistDeletionChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion, ChecklistRecord Checklist, long DeletedItems, bool Changed);

public sealed partial class ChecklistService
{
    public async Task<WorkOperation<ChecklistDeletionChange>> DeleteAsync(Guid cardId, Guid checklistId, Guid actor, DeleteChecklistInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistDeletionChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistDelete", checklistId, new { cardId, input }, "checklist_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId && receipt.CardId == cardId &&
                receipt.Checklist.Id == checklistId && receipt.Checklist.CardId == cardId && receipt.Checklist.OrganizationId == hint.OrganizationId && receipt.Checklist.DeletedAt is not null)
                && await AdmitDeletion(hint, actor, ct) && await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct, true) is not null,
            async () =>
            {
                if (!input.Confirmed) return WorkOperation<ChecklistDeletionChange>.Failure("delete_confirmation_required");
                if (input.CardVersion < 1 || input.Version < 1) return WorkOperation<ChecklistDeletionChange>.Failure("invalid_checklist_version");
                var current = await work.FindCardAsync(cardId, ct);
                var child = await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct, true);
                if (current is null || child is null) return WorkOperation<ChecklistDeletionChange>.Failure("checklist_not_found");
                if (current.Version != input.CardVersion || child.Version != input.Version) return WorkOperation<ChecklistDeletionChange>.Failure("version_conflict");
                if (child.DeletedAt is not null)
                {
                    if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistDeletionChange>.Failure("session_unavailable");
                    return WorkOperation<ChecklistDeletionChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, child, 0, false));
                }
                var now = clock.UtcNow;
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, now, ct);
                if (updated is null) return WorkOperation<ChecklistDeletionChange>.Failure("version_conflict");
                var deleted = await checklists.DeleteAsync(hint.OrganizationId, cardId, checklistId, child.Version, actor, correlationId, now, ct);
                if (deleted is null) return WorkOperation<ChecklistDeletionChange>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "CHECKLIST_DELETED", "Checklist", checklistId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CHECKLIST_DELETED",
                    "Card", cardId, updated.Version, correlationId, now), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistDeletionChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistDeletionChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, deleted.Checklist, deleted.DeletedItems, true));
            }, ct);
    }
}
