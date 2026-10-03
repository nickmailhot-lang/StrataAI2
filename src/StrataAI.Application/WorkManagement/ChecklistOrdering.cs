using System.Text.Json.Serialization;

namespace StrataAI.Application.WorkManagement;

public sealed record ChecklistPositionInput([property: JsonRequired] Guid? BeforeId, long CardVersion, long Version);
public sealed record ChecklistItemPositionInput([property: JsonRequired] Guid? BeforeId, long CardVersion, long ChecklistVersion, long Version);

public sealed partial class ChecklistService
{
    public async Task<WorkOperation<ChecklistChange>> ReorderAsync(Guid cardId, Guid checklistId, Guid actor, ChecklistPositionInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistPosition", checklistId, new { cardId, input }, "checklist_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId &&
                receipt.CardId == cardId && receipt.Checklist.Id == checklistId && receipt.Checklist.OrganizationId == hint.OrganizationId && receipt.Checklist.CardId == cardId)
                && await Admit(hint, actor, true, ct) && await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct) is not null,
            async () =>
            {
                if (input.CardVersion < 1 || input.Version < 1) return WorkOperation<ChecklistChange>.Failure("invalid_checklist_version");
                var current = await work.FindCardAsync(cardId, ct);
                var child = await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct);
                if (current is null || child is null) return WorkOperation<ChecklistChange>.Failure("checklist_not_found");
                if (current.Version != input.CardVersion || child.Version != input.Version) return WorkOperation<ChecklistChange>.Failure("version_conflict");
                string rank;
                try { rank = await checklists.PositionRankAsync(hint.OrganizationId, cardId, checklistId, child.Rank, input.BeforeId, false, ct); }
                catch (RankSpaceExhaustedException) { return WorkOperation<ChecklistChange>.Failure("rank_space_exhausted"); }
                catch (ArgumentException) { return WorkOperation<ChecklistChange>.Failure("invalid_move_position"); }
                if (rank == child.Rank)
                {
                    if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistChange>.Failure("session_unavailable");
                    return WorkOperation<ChecklistChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, child, false));
                }
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, clock.UtcNow, ct);
                if (updated is null) return WorkOperation<ChecklistChange>.Failure("version_conflict");
                var renamed = await checklists.UpdateRankAsync(hint.OrganizationId, cardId, checklistId, rank, child.Version, clock.UtcNow, ct);
                if (renamed is null) return WorkOperation<ChecklistChange>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "CHECKLIST_UPDATED", "Checklist", checklistId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CHECKLIST_UPDATED",
                    "Card", cardId, updated.Version, correlationId, clock.UtcNow), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, renamed, true));
            }, ct);
    }
    public async Task<WorkOperation<ChecklistItemChange>> ReorderItemAsync(Guid cardId, Guid checklistId, Guid itemId, Guid actor, ChecklistItemPositionInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistItemChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistItemPosition", itemId, new { cardId, checklistId, input }, "checklist_item_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId &&
                receipt.CardId == cardId && receipt.Checklist.Id == checklistId && receipt.Checklist.CardId == cardId &&
                receipt.Checklist.OrganizationId == hint.OrganizationId && receipt.Item.Id == itemId && receipt.Item.ChecklistId == checklistId && receipt.Item.OrganizationId == hint.OrganizationId)
                && await Admit(hint, actor, true, ct) && await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct) is not null
                && await checklists.FindItemAsync(hint.OrganizationId, checklistId, itemId, ct) is not null,
            async () =>
            {
                if (input.CardVersion < 1 || input.ChecklistVersion < 1 || input.Version < 1) return WorkOperation<ChecklistItemChange>.Failure("invalid_checklist_version");
                var current = await work.FindCardAsync(cardId, ct);
                var child = await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct);
                var item = await checklists.FindItemAsync(hint.OrganizationId, checklistId, itemId, ct);
                if (current is null || child is null || item is null) return WorkOperation<ChecklistItemChange>.Failure("checklist_item_not_found");
                if (current.Version != input.CardVersion || child.Version != input.ChecklistVersion || item.Version != input.Version)
                    return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                string rank;
                try { rank = await checklists.PositionRankAsync(hint.OrganizationId, checklistId, itemId, item.Rank, input.BeforeId, true, ct); }
                catch (RankSpaceExhaustedException) { return WorkOperation<ChecklistItemChange>.Failure("rank_space_exhausted"); }
                catch (ArgumentException) { return WorkOperation<ChecklistItemChange>.Failure("invalid_move_position"); }
                if (rank == item.Rank)
                {
                    if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                    return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, child, item, false));
                }
                var now = clock.UtcNow;
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, now, ct);
                if (updated is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var updatedChild = await checklists.RenameAsync(hint.OrganizationId, cardId, checklistId, child.Title, child.Version, now, ct);
                if (updatedChild is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var updatedItem = await checklists.UpdateItemRankAsync(hint.OrganizationId, checklistId, itemId, rank, item.Version, now, ct);
                if (updatedItem is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "CHECKLIST_ITEM_UPDATED", "ChecklistItem", itemId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CHECKLIST_ITEM_UPDATED",
                    "Card", cardId, updated.Version, correlationId, now), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, updatedChild, updatedItem, true));
            }, ct);
    }
}
