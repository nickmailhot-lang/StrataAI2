using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<Guid, BoardLabelRecord> _labels = [];
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
            _labels[labelId] = deleted; return Task.FromResult<BoardLabelRecord?>(deleted);
        }
    }
}
