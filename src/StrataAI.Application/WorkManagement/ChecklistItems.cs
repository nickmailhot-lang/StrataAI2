using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record ChecklistItemRecord(Guid Id, Guid OrganizationId, Guid ChecklistId, string Text, string Rank,
    bool Completed, DateTimeOffset? CompletedAt, Guid? CompletedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version, DateTimeOffset? DeletedAt);
public sealed record CreateChecklistItemInput(string? Text, long CardVersion, long ChecklistVersion);
public sealed record ChecklistItemChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    ChecklistRecord Checklist, ChecklistItemRecord Item, bool Changed);
public sealed record ChecklistItemPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    ChecklistSummary Summary, IReadOnlyList<ChecklistItemRecord> Items, string? NextCursor, bool CanEdit);

public sealed partial class ChecklistService
{
    public async Task<WorkOperation<ChecklistItemPage>> ListItemsAsync(Guid cardId, Guid checklistId, Guid actor, string? after, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistItemPage>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, null, "ChecklistItemRead", checklistId, new { cardId, after }, "checklist_not_found"),
            async _ => await Admit(hint, actor, false, ct) && await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct) is not null,
            async () =>
            {
                string? rank = null; Guid? id = null;
                if (after is not null)
                {
                    var parts = after.Split('/');
                    if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out var parent) || parent != checklistId ||
                        !RankToken.IsValid(parts[1]) || !Guid.TryParseExact(parts[2], "D", out var parsed) || parsed == Guid.Empty)
                        return WorkOperation<ChecklistItemPage>.Failure("invalid_checklist_cursor");
                    rank = parts[1]; id = parsed;
                }
                var current = await work.FindCardAsync(cardId, ct);
                var scope = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
                var list = current is null ? null : await work.FindListAsync(current.ListId, ct);
                var child = await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct);
                if (current is null || list is null || scope.Value is null || child is null) return WorkOperation<ChecklistItemPage>.Failure("checklist_not_found");
                var rows = await checklists.ListItemsAsync(hint.OrganizationId, checklistId, rank, id, ct);
                var summary = await checklists.GetSummaryAsync(hint.OrganizationId, cardId, checklistId, ct);
                if (summary is null || !await Admit(hint, actor, false, ct) || !await actors.VerifyAsync(actor, ct))
                    return WorkOperation<ChecklistItemPage>.Failure("checklist_not_found");
                var items = rows.Take(50).ToArray();
                var cursor = rows.Count > 50 ? $"{checklistId:D}/{items[^1].Rank}/{items[^1].Id:D}" : null;
                return WorkOperation<ChecklistItemPage>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, summary, items, cursor,
                    scope.Value.Access.CanEdit && scope.Value.Board.LifecycleState == BoardLifecycleState.Active &&
                    current.LifecycleState == WorkItemLifecycleState.Active && list.LifecycleState == WorkItemLifecycleState.Active));
            }, ct);
    }
    public async Task<WorkOperation<ChecklistItemChange>> CreateItemAsync(Guid cardId, Guid checklistId, Guid actor, CreateChecklistItemInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistItemChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistItemCreate", checklistId, new { cardId, input }, "checklist_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId &&
                receipt.CardId == cardId && receipt.Checklist.Id == checklistId && receipt.Checklist.CardId == cardId &&
                receipt.Checklist.OrganizationId == hint.OrganizationId && receipt.Item.ChecklistId == checklistId && receipt.Item.OrganizationId == hint.OrganizationId)
                && await Admit(hint, actor, true, ct) && await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct) is not null
                && (receipt is null || await checklists.FindItemAsync(hint.OrganizationId, checklistId, receipt.Item.Id, ct) is not null),
            async () =>
            {
                if (input.CardVersion < 1 || input.ChecklistVersion < 1) return WorkOperation<ChecklistItemChange>.Failure("invalid_checklist_version");
                var current = await work.FindCardAsync(cardId, ct);
                var child = await checklists.FindAsync(hint.OrganizationId, cardId, checklistId, ct);
                if (current is null || child is null) return WorkOperation<ChecklistItemChange>.Failure("checklist_not_found");
                if (current.Version != input.CardVersion || child.Version != input.ChecklistVersion) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                string text;
                try { text = new ChecklistItem(Guid.NewGuid(), hint.OrganizationId, checklistId, input.Text!, RankToken.Initial(), clock.UtcNow).Text; }
                catch (ArgumentException) { return WorkOperation<ChecklistItemChange>.Failure("invalid_checklist_item_text"); }
                string rank;
                try { rank = await checklists.NextItemRankAsync(hint.OrganizationId, checklistId, ct); }
                catch (RankSpaceExhaustedException) { return WorkOperation<ChecklistItemChange>.Failure("rank_space_exhausted"); }
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, current.Version, clock.UtcNow, ct);
                if (updated is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var updatedChild = await checklists.RenameAsync(hint.OrganizationId, cardId, checklistId, child.Title, child.Version, clock.UtcNow, ct);
                if (updatedChild is null) return WorkOperation<ChecklistItemChange>.Failure("version_conflict");
                var item = await checklists.CreateItemAsync(hint.OrganizationId, checklistId, text, rank, clock.UtcNow, ct);
                await work.AppendAuditAsync(hint.OrganizationId, actor, "CHECKLIST_ITEM_CREATED", "ChecklistItem", item.Id, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CHECKLIST_ITEM_CREATED",
                    "Card", cardId, updated.Version, correlationId, clock.UtcNow), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistItemChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistItemChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, updatedChild, item, true));
            }, ct);
    }
}
