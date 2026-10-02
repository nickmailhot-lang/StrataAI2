using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<BoardLabelRecord?> MoveLabelAsync(Guid labelId, Guid? beforeLabelId, long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_labels.TryGetValue(labelId, out var label) || label.Deleted || label.Version != version) return Task.FromResult<BoardLabelRecord?>(null);
            BoardLabelRecord? anchor = null;
            if (beforeLabelId is not null && (!_labels.TryGetValue(beforeLabelId.Value, out anchor) || anchor.Deleted || anchor.BoardId != label.BoardId || anchor.Id == labelId))
                return Task.FromResult<BoardLabelRecord?>(null);
            var previous = _labels.Values.Where(item => item.BoardId == label.BoardId && !item.Deleted && item.Id != labelId
                && (anchor is null || string.CompareOrdinal(item.Rank, anchor.Rank) < 0 || item.Rank == anchor.Rank && item.Id.CompareTo(anchor.Id) < 0))
                .OrderBy(item => item.Rank, StringComparer.Ordinal).ThenBy(item => item.Id).LastOrDefault();
            if (anchor is not null && previous?.Rank == anchor.Rank) throw new RankSpaceExhaustedException();
            var rank = anchor is null ? RankToken.After(previous?.Rank) : RankToken.Between(previous?.Rank, anchor.Rank);
            return UpdateLabelAsync(labelId, label.Name, label.Color, rank, version, now, cancellationToken);
        }
    }
    private IReadOnlyDictionary<Guid, CardLabelPreview> LabelPreviews(IReadOnlyList<BoardListSnapshot> lists)
    {
        var cards = lists.SelectMany(list => list.Cards).ToDictionary(card => card.Id);
        return _cardLabels.Where(a => cards.TryGetValue(a.CardId, out var card) && _labels.TryGetValue(a.LabelId, out var label)
                && !label.Deleted && label.OrganizationId == card.OrganizationId && label.BoardId == card.BoardId)
            .GroupBy(a => a.CardId).ToDictionary(group => group.Key, group => new CardLabelPreview(
                group.Select(a => _labels[a.LabelId]).OrderBy(label => label.Rank, StringComparer.Ordinal).ThenBy(label => label.Id)
                    .Take(6).Select(label => new CardLabelIndicator(label.Id, label.Name, label.Color)).ToArray(), group.LongCount()));
    }
    private readonly Dictionary<Guid, BoardLabelRecord> _labels = [];
    private readonly HashSet<(Guid CardId, Guid LabelId)> _cardLabels = [];
    public Task<IReadOnlyList<BoardLabelRecord>> ListCardLabelsAsync(Guid cardId, Guid? after, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card)) return Task.FromResult<IReadOnlyList<BoardLabelRecord>>([]);
            return Task.FromResult<IReadOnlyList<BoardLabelRecord>>(_labels.Values.Where(label => !label.Deleted
                && label.OrganizationId == card.OrganizationId && label.BoardId == card.BoardId && _cardLabels.Contains((cardId, label.Id))
                && (after is null || label.Id.CompareTo(after.Value) > 0)).OrderBy(label => label.Id).Take(51).ToArray());
        }
    }
    public Task<CardLabelChange?> SetCardLabelAsync(Guid cardId, Guid labelId, bool assigned, long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) || card.Version != version || card.LifecycleState != WorkItemLifecycleState.Active
                || !_labels.TryGetValue(labelId, out var label) || label.Deleted || label.BoardId != card.BoardId || label.OrganizationId != card.OrganizationId)
                return Task.FromResult<CardLabelChange?>(null);
            var changed = assigned ? _cardLabels.Add((cardId, labelId)) : _cardLabels.Remove((cardId, labelId));
            if (changed) { card = card with { Version = card.Version + 1, UpdatedAt = now }; _cards[cardId] = card; }
            return Task.FromResult<CardLabelChange?>(new(card, labelId, assigned, changed));
        }
    }
    public Task<IReadOnlyList<BoardLabelRecord>> ListLabelsAsync(Guid boardId, Guid? after, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<BoardLabelRecord>>(_labels.Values.Where(label => label.BoardId == boardId
            && !label.Deleted && (after is null || label.Id.CompareTo(after.Value) > 0)).OrderBy(label => label.Id).Take(51).ToArray());
    }
    public Task<BoardLabelRecord?> FindLabelAsync(Guid labelId, CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        lock (_sync) { _labels.TryGetValue(labelId, out var label); return Task.FromResult(includeDeleted || label?.Deleted != true ? label : null); }
    }
    public Task<BoardLabelRecord> CreateLabelAsync(Guid boardId, Guid id, string name, string color, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var rank = RankToken.After(_labels.Values.Where(label => label.BoardId == boardId && !label.Deleted)
                .Select(label => label.Rank).Order(StringComparer.Ordinal).LastOrDefault());
            var label = new BoardLabelRecord(id, _boards[boardId].OrganizationId, boardId, name, color, rank, false, now, now, 1);
            _labels.Add(id, label); return Task.FromResult(label);
        }
    }
    public Task<BoardLabelRecord?> UpdateLabelAsync(Guid labelId, string name, string color, string rank, long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_labels.TryGetValue(labelId, out var label) || label.Deleted || label.Version != version) return Task.FromResult<BoardLabelRecord?>(null);
            var updated = label with { Name = name, Color = color, Rank = rank, UpdatedAt = now, Version = version + 1 };
            _labels[labelId] = updated; return Task.FromResult<BoardLabelRecord?>(updated);
        }
    }
    public Task<BoardLabelRecord?> DeleteLabelAsync(Guid labelId, long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_labels.TryGetValue(labelId, out var label) || label.Deleted || label.Version != version) return Task.FromResult<BoardLabelRecord?>(null);
            var deleted = label with { Deleted = true, UpdatedAt = now, Version = version + 1 };
            foreach (var association in _cardLabels.Where(item => item.LabelId == labelId).ToArray())
            {
                _cardLabels.Remove(association);
                if (_cards.TryGetValue(association.CardId, out var card) && card.LifecycleState != WorkItemLifecycleState.Deleted)
                    _cards[card.Id] = card with { Version = card.Version + 1, UpdatedAt = now };
            }
            _labels[labelId] = deleted; return Task.FromResult<BoardLabelRecord?>(deleted);
        }
    }
}
