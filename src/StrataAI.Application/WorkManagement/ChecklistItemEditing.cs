using System.Text.Json.Serialization;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record UpdateChecklistItemInput(string? Text, [property: JsonRequired] bool Completed, long CardVersion, long ChecklistVersion, long Version);

public sealed partial class ChecklistService
{
    public async Task<WorkOperation<ChecklistItemChange>> UpdateItemAsync(Guid cardId, Guid checklistId, Guid itemId, Guid actor, UpdateChecklistItemInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistItemChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistItemUpdate", itemId, new { cardId, checklistId, input }, "checklist_item_not_found"),
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
                string text;
                try { text = new ChecklistItem(itemId, hint.OrganizationId, checklistId, input.Text!, item.Rank, clock.UtcNow).Text; }
                catch (ArgumentException) { return WorkOperation<ChecklistItemChange>.Failure("invalid_checklist_item_text"); }
                var textChanged = text != item.Text; var completionChanged = input.Completed != item.Completed;
                if (!textChanged && !completionChanged)
                {
                    if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                    return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, child, item, false));
                }
                var now = clock.UtcNow;
                var completedAt = completionChanged ? input.Completed ? now : (DateTimeOffset?)null : item.CompletedAt;
                var completedBy = completionChanged ? input.Completed ? actor : (Guid?)null : item.CompletedBy;
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, now, ct);
                if (updated is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var updatedChild = await checklists.RenameAsync(hint.OrganizationId, cardId, checklistId, child.Title, child.Version, now, ct);
                if (updatedChild is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var updatedItem = await checklists.UpdateItemAsync(hint.OrganizationId, checklistId, itemId, text, input.Completed, completedAt, completedBy, item.Version, now, ct);
                if (updatedItem is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var eventTypes = new List<string>();
                if (textChanged) eventTypes.Add("CHECKLIST_ITEM_UPDATED");
                if (completionChanged) eventTypes.Add(input.Completed ? "CHECKLIST_ITEM_COMPLETED" : "CHECKLIST_ITEM_UNCOMPLETED");
                foreach (var type in eventTypes)
                {
                    await work.AppendAuditAsync(hint.OrganizationId, actor, type, "ChecklistItem", itemId, correlationId, ct);
                    await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, type,
                        "Card", cardId, updated.Version, correlationId, now), ct);
                }
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, updatedChild, updatedItem, true));
            }, ct);
    }
}
