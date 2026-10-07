using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Onboarding;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed record DemoOrganizationDeletionGraphPage(int Count, IReadOnlyList<WorkEvent> Events,
    IReadOnlyList<DemoInvitationAudit> Audits);

internal sealed partial class InMemoryWorkManagementStore
{
    // Raw tenant graph, including archived children and references on existing
    // tombstones. Normal authorized UI directories must not truncate deletion.
    internal DemoOrganizationDeletionGraphPage ApplyOrganizationDeletionGraphPage(Guid organization, Guid actor,
        int limit, DateTimeOffset now, string correlation, CancellationToken ct)
    {
        if (!transactionScope.OwnsOrganizationCommand(organization) || actor == Guid.Empty || limit is < 1 or > 128)
            throw new InvalidOperationException("Demo graph effects require the owning Organization transaction.");
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            List<WorkEvent> changes = []; List<DemoInvitationAudit> audits = [];
            void Event(Guid board, string type, string entityType, Guid entity, long version, DateTimeOffset at)
                => changes.Add(new(Guid.NewGuid(), organization, board, actor, type, entityType, entity, version, correlation, at));
            DateTimeOffset At(params DateTimeOffset[] values) => values.Append(now).Max();
            var attachments = _attachmentMetadata.Values.Where(a => a.OrganizationId == organization
                && a.LifecycleState != AttachmentLifecycleState.Deleted).OrderBy(a => a.Id).Take(limit).ToArray();
            if (attachments.Length > 0)
            {
                foreach (var a in attachments)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_cards.TryGetValue(a.CardId, out var card) || card.OrganizationId != organization
                        || !_boards.TryGetValue(card.BoardId, out var board) || board.OrganizationId != organization)
                        throw new InvalidOperationException("Demo graph attachment parent is unavailable.");
                    var at = At(a.UpdatedAt, card.UpdatedAt);
                    var cover = _cardCovers.GetValueOrDefault(card.Id) == a.Id;
                    if (card.LifecycleState != WorkItemLifecycleState.Deleted || cover)
                    {
                        card = card with { Version = checked(card.Version + 1), UpdatedAt = at };
                        _cards[card.Id] = card;
                    }
                    if (cover) _cardCovers.Remove(card.Id);
                    _attachmentMetadata[a.Id] = a with { LifecycleState = AttachmentLifecycleState.Deleted,
                        Version = checked(a.Version + 1), UpdatedAt = at, DeletedAt = at, DeletedBy = actor };
                    audits.Add(new(Guid.NewGuid(), organization, actor, "ATTACHMENT_DELETED", "Attachment", a.Id, correlation, at));
                    if (cover) Event(card.BoardId, "CARD_COVER_CHANGED", "Card", card.Id, card.Version, at);
                    Event(card.BoardId, "ATTACHMENT_DELETED", "Card", card.Id, card.Version, at);
                }
                return new(attachments.Length, changes, audits);
            }
            var cards = _cards.Values.Where(c => c.OrganizationId == organization
                && (c.LifecycleState != WorkItemLifecycleState.Deleted || _cardCovers.ContainsKey(c.Id)))
                .OrderBy(c => c.Id).Take(limit).ToArray();
            if (cards.Length > 0)
            {
                foreach (var card in cards)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_boards.TryGetValue(card.BoardId, out var parentBoard) || parentBoard.OrganizationId != organization
                        || !_lists.TryGetValue(card.ListId, out var parentList) || parentList.OrganizationId != organization
                        || parentList.BoardId != card.BoardId)
                        throw new InvalidOperationException("Demo graph Card parent is unavailable.");
                    var deleted = card.LifecycleState == WorkItemLifecycleState.Deleted; var at = At(card.UpdatedAt);
                    var updated = card with { LifecycleState = WorkItemLifecycleState.Deleted,
                        Version = checked(card.Version + 1), UpdatedAt = at,
                        DeletedAt = deleted ? card.DeletedAt : at, DeletedBy = deleted ? card.DeletedBy : actor };
                    _cards[card.Id] = updated;
                    if (_cardCovers.Remove(card.Id)) Event(card.BoardId, "CARD_COVER_CHANGED", "Card", card.Id, updated.Version, at);
                    if (!deleted) Event(card.BoardId, "CARD_DELETED", "Card", card.Id, updated.Version, at);
                }
                return new(cards.Length, changes, audits);
            }
            var lists = _lists.Values.Where(l => l.OrganizationId == organization && l.LifecycleState != WorkItemLifecycleState.Deleted)
                .OrderBy(l => l.Id).Take(limit).ToArray();
            if (lists.Length > 0)
            {
                foreach (var list in lists)
                {
                    ct.ThrowIfCancellationRequested(); var at = At(list.UpdatedAt);
                    if (!_boards.TryGetValue(list.BoardId, out var parentBoard) || parentBoard.OrganizationId != organization)
                        throw new InvalidOperationException("Demo graph List parent is unavailable.");
                    var updated = list with { LifecycleState = WorkItemLifecycleState.Deleted,
                        Version = checked(list.Version + 1), UpdatedAt = at, DeletedAt = at, DeletedBy = actor };
                    _lists[list.Id] = updated;
                    Event(list.BoardId, "LIST_DELETED", "List", list.Id, updated.Version, at);
                }
                return new(lists.Length, changes, audits);
            }
            var boards = _boards.Values.Where(b => b.OrganizationId == organization
                && (b.LifecycleState != BoardLifecycleState.Deleted || b.BackgroundType == "IMAGE"))
                .OrderBy(b => b.Id).Take(limit).ToArray();
            foreach (var board in boards)
            {
                ct.ThrowIfCancellationRequested(); var at = At(board.UpdatedAt);
                var deleted = board.LifecycleState == BoardLifecycleState.Deleted;
                var updated = board with { LifecycleState = BoardLifecycleState.Deleted, BackgroundType = "COLOR",
                    BackgroundValue = board.BackgroundType == "IMAGE" ? null : board.BackgroundValue,
                    Version = checked(board.Version + 1), UpdatedAt = at,
                    DeletedAt = deleted ? board.DeletedAt : at, DeletedBy = deleted ? board.DeletedBy : actor };
                _boards[board.Id] = updated;
                var type = deleted ? "BOARD_UPDATED" : "BOARD_DELETED";
                CaptureBoardAuthorityProof(updated, type, at);
                Event(board.Id, type, "Board", board.Id, updated.Version, at);
            }
            // Integrity, upload/preview evidence, comments, assignments, labels,
            // checklist and historical image records remain intact.
            return new(boards.Length, changes, audits);
        }
    }
}
