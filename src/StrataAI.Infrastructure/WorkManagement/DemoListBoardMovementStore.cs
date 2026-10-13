using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IListBoardMovementStore
{
    public async Task<ListBoardTransfer?> TransferListBoardAsync(Guid organization, Guid sourceBoard, Guid list,
        Guid destinationBoard, long expectedVersion, DateTimeOffset now, bool requireVerifiedEmail,
        Guid? beforeList, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!transactionScope.Owns(organization)) throw new InvalidOperationException("List transfer requires its owning command transaction.");
        Guid[] assignedUsers;
        lock (_sync)
        {
            if (!_lists.TryGetValue(list, out var initial) || initial.OrganizationId != organization || initial.BoardId != sourceBoard) return null;
            var ids = _cards.Values.Where(c => c.OrganizationId == organization && c.BoardId == sourceBoard && c.ListId == list).Select(c => c.Id).ToHashSet();
            assignedUsers = _cardMembers.Keys.Where(k => ids.Contains(k.CardId)).Select(k => k.UserId).Distinct().ToArray();
        }
        var eligible = new HashSet<Guid>();
        foreach (var user in assignedUsers)
            if (await IsAssignableBoardMemberAsync(destinationBoard, user, requireVerifiedEmail, ct)) eligible.Add(user);
        lock (_sync)
        {
            ct.ThrowIfCancellationRequested();
            if (sourceBoard == destinationBoard || !_boards.TryGetValue(sourceBoard, out var source) || !_boards.TryGetValue(destinationBoard, out var destination)
                || source.OrganizationId != organization || destination.OrganizationId != organization
                || source.LifecycleState != BoardLifecycleState.Active || destination.LifecycleState != BoardLifecycleState.Active
                || !_lists.TryGetValue(list, out var current) || current.OrganizationId != organization || current.BoardId != sourceBoard
                || current.Version != expectedVersion || current.LifecycleState != WorkItemLifecycleState.Active || now < current.UpdatedAt
                || configurationState.IsCurrentIntake(organization, list)) return null;
            var siblings = _lists.Values.Where(l => l.BoardId == destinationBoard && l.LifecycleState == WorkItemLifecycleState.Active)
                .OrderBy(l => l.Rank, StringComparer.Ordinal).ThenBy(l => l.Id).ToArray();
            string rank;
            if (beforeList is { } anchor)
            {
                var position = Array.FindIndex(siblings, l => l.Id == anchor);
                if (position < 0) return null;
                rank = RankToken.Between(position == 0 ? null : siblings[position - 1].Rank, siblings[position].Rank);
            }
            else rank = RankToken.After(siblings.LastOrDefault()?.Rank);
            var cards = _cards.Values.Where(c => c.OrganizationId == organization && c.BoardId == sourceBoard && c.ListId == list).ToArray();
            if (cards.Any(c => now < c.UpdatedAt)) return null;
            var ids = cards.Select(c => c.Id).ToHashSet();
            var associations = _cardLabels.Where(a => ids.Contains(a.CardId)).ToArray();
            var plan = CardLabelTransferPlan.Create(organization, sourceBoard, ids,
                associations.Select(a => _labels[a.LabelId]), associations.Select(a => new CardLabelTransferAssociation(a.CardId, a.LabelId)));
            var last = _labels.Values.Where(l => l.OrganizationId == organization && l.BoardId == destinationBoard && !l.Deleted)
                .Select(l => l.Rank).Order(StringComparer.Ordinal).LastOrDefault();
            var definitions = new Dictionary<Guid, BoardLabelRecord>();
            foreach (var label in plan.Labels)
            {
                last = RankToken.After(last);
                definitions.Add(label.Id, label with { Id = Guid.NewGuid(), BoardId = destinationBoard, Rank = last, CreatedAt = now, UpdatedAt = now, Version = 1 });
            }
            var transferred = current with { BoardId = destinationBoard, Rank = rank, UpdatedAt = now, Version = checked(current.Version + 1) };
            var moved = cards.Select(c => c with { BoardId = destinationBoard, UpdatedAt = now, Version = checked(c.Version + 1) }).ToArray();
            // All allocation/validation precedes mutation. The owning Demo unit
            // captures these same dictionaries/sets for later publication rollback.
            _lists[list] = transferred;
            foreach (var card in moved) _cards[card.Id] = card;
            foreach (var label in definitions.Values) _labels.Add(label.Id, label);
            _cardLabels.RemoveWhere(a => ids.Contains(a.CardId));
            foreach (var association in plan.Associations) _cardLabels.Add((association.CardId, definitions[association.LabelId].Id));
            foreach (var assignment in _cardMembers.Keys.Where(k => ids.Contains(k.CardId) && !eligible.Contains(k.UserId)).ToArray()) _cardMembers.Remove(assignment);
            return new(transferred, Array.AsReadOnly(cards.Select(c => new ListTransferredCard(c.Id, c.Version, c.LifecycleState)).ToArray()));
        }
    }
}
