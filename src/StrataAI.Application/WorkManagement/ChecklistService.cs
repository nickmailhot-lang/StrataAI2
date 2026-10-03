using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record ChecklistRecord(Guid Id, Guid OrganizationId, Guid CardId, string Title, string Rank,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version, DateTimeOffset? DeletedAt);
public sealed record ChecklistSummary(ChecklistRecord Checklist, long Completed, long Total)
{
    public decimal Percent => Total == 0 ? 0 : Completed * 100m / Total;
}
public sealed record ChecklistPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    IReadOnlyList<ChecklistSummary> Items, string? NextCursor, bool CanEdit);
public sealed record CreateChecklistInput(string? Title, long CardVersion);
public sealed record ChecklistChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion, ChecklistRecord Checklist, bool Changed);
public interface IChecklistStore
{
    Task<IReadOnlyList<ChecklistSummary>> ListAsync(Guid organization, Guid card, string? afterRank, Guid? afterId, CancellationToken ct);
    Task<string> NextRankAsync(Guid organization, Guid card, CancellationToken ct);
    Task<ChecklistRecord> CreateAsync(Guid organization, Guid card, string title, string rank, DateTimeOffset now, CancellationToken ct);
}

public sealed class ChecklistService(IWorkManagementStore work, IChecklistStore checklists, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, IWorkCommandContext context, ICommandActorAuthorization actors,
    IClock clock, IWorkEventStore events)
{
    public async Task<WorkOperation<ChecklistPage>> ListAsync(Guid cardId, Guid actor, string? after, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistPage>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, null, "ChecklistRead", cardId, new { after }, "card_not_found"),
            async _ => await Admit(hint, actor, false, ct), async () =>
            {
                string? rank = null; Guid? id = null;
                if (after is not null)
                {
                    var parts = after.Split('/');
                    if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out var parent) || parent != cardId ||
                        !RankToken.IsValid(parts[1]) || !Guid.TryParseExact(parts[2], "D", out var parsed) || parsed == Guid.Empty)
                        return WorkOperation<ChecklistPage>.Failure("invalid_checklist_cursor");
                    rank = parts[1]; id = parsed;
                }
                var current = await work.FindCardAsync(cardId, ct);
                var scope = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
                var list = current is null ? null : await work.FindListAsync(current.ListId, ct);
                var rows = await checklists.ListAsync(hint.OrganizationId, cardId, rank, id, ct);
                if (current is null || list is null || scope.Value is null || !await Admit(hint, actor, false, ct) || !await actors.VerifyAsync(actor, ct))
                    return WorkOperation<ChecklistPage>.Failure("card_not_found");
                var items = rows.Take(50).ToArray();
                var cursor = rows.Count > 50 ? $"{cardId:D}/{items[^1].Checklist.Rank}/{items[^1].Checklist.Id:D}" : null;
                return WorkOperation<ChecklistPage>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, items, cursor,
                    scope.Value.Access.CanEdit && scope.Value.Board.LifecycleState == BoardLifecycleState.Active &&
                    current.LifecycleState == WorkItemLifecycleState.Active && list.LifecycleState == WorkItemLifecycleState.Active));
            }, ct);
    }
    public async Task<WorkOperation<ChecklistChange>> CreateAsync(Guid cardId, Guid actor, CreateChecklistInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<ChecklistChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "ChecklistCreate", cardId, input, "card_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId &&
                receipt.CardId == cardId && receipt.Checklist.OrganizationId == hint.OrganizationId && receipt.Checklist.CardId == cardId)
                && await Admit(hint, actor, true, ct), async () =>
            {
                if (input.CardVersion < 1) return WorkOperation<ChecklistChange>.Failure("invalid_checklist_version");
                string title;
                try { title = new Checklist(Guid.NewGuid(), hint.OrganizationId, cardId, input.Title!, RankToken.Initial(), clock.UtcNow).Title; }
                catch (ArgumentException) { return WorkOperation<ChecklistChange>.Failure("invalid_checklist_title"); }
                var current = await work.FindCardAsync(cardId, ct);
                if (current is null || current.Version != input.CardVersion) return WorkOperation<ChecklistChange>.Failure("version_conflict");
                string rank;
                try { rank = await checklists.NextRankAsync(hint.OrganizationId, cardId, ct); }
                catch (RankSpaceExhaustedException) { return WorkOperation<ChecklistChange>.Failure("rank_space_exhausted"); }
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, input.CardVersion, clock.UtcNow, ct);
                if (updated is null) return WorkOperation<ChecklistChange>.Failure("version_conflict");
                var created = await checklists.CreateAsync(hint.OrganizationId, cardId, title, rank, clock.UtcNow, ct);
                await work.AppendAuditAsync(hint.OrganizationId, actor, "CHECKLIST_CREATED", "Checklist", created.Id, correlationId, ct);
                // Card aggregate invalidation carries no child title or item data.
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "CHECKLIST_CREATED",
                    "Card", cardId, updated.Version, correlationId, clock.UtcNow), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<ChecklistChange>.Failure("session_unavailable");
                return WorkOperation<ChecklistChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, created, true));
            }, ct);
    }
    private async Task<bool> Admit(CardRecord hint, Guid actor, bool editing, CancellationToken ct)
    {
        if (!(editing ? await work.AcquireCommandScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct)
            : await work.AcquireBoardReadScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct))) return false;
        var scope = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
        if (scope.Value is null || !scope.Value.Access.CanView || scope.Value.Board.OrganizationId != hint.OrganizationId ||
            editing && (!scope.Value.Access.CanEdit || scope.Value.Board.LifecycleState != BoardLifecycleState.Active)) return false;
        var card = await work.FindCardAsync(hint.Id, ct);
        if (card is null || card.OrganizationId != hint.OrganizationId || card.BoardId != hint.BoardId || card.ListId != hint.ListId ||
            editing && card.LifecycleState != WorkItemLifecycleState.Active) return false;
        var list = await work.FindListAsync(card.ListId, ct);
        return list is not null && list.OrganizationId == hint.OrganizationId && list.BoardId == hint.BoardId &&
            (!editing || list.LifecycleState == WorkItemLifecycleState.Active);
    }
}
