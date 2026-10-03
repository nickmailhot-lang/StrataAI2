using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : ICardCommentStore
{
    private readonly Dictionary<Guid,CardCommentRecord> _comments = [];
    private void RequireCommentScope(Guid organization)
    {
        if (!transactionScope.Owns(organization)) throw new InvalidOperationException("Comment storage requires the owning scope.");
    }
    public async Task<CardCommentRecord> CreateAsync(Guid id, Guid organization, Guid card, Guid author, string content, DateTimeOffset at, CancellationToken ct)
    {
        RequireCommentScope(organization); ct.ThrowIfCancellationRequested();
        var value = new CardComment(id, organization, card, author, content, AttachmentMetadataMapping.DatabaseTimestamp(at));
        if (await organizations.FindMembershipAsync(organization, author, ct) is null) throw new InvalidOperationException("Comment author unavailable.");
        lock (_sync)
        {
            if (!_cards.TryGetValue(card, out var parent) || parent.OrganizationId != organization) throw new InvalidOperationException("Comment parent unavailable.");
            var row = new CardCommentRecord(id, organization, card, author, value.Content, value.CreatedAt, value.UpdatedAt, 1, null, null, null);
            _comments.Add(id, row); return row;
        }
    }
    public Task<CardCommentRecord?> FindAsync(Guid organization, Guid card, Guid comment, CancellationToken ct)
    {
        RequireCommentScope(organization); ct.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_comments.TryGetValue(comment, out var row) && row.OrganizationId == organization && row.CardId == card ? row : null);
    }
    public Task<IReadOnlyList<CardCommentRecord>> ListAsync(Guid organization, Guid card, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct)
    {
        RequireCommentScope(organization); ct.ThrowIfCancellationRequested();
        if (beforeCreatedAt.HasValue != beforeId.HasValue || beforeId == Guid.Empty) throw new ArgumentException("Comment cursor is invalid.");
        lock (_sync) return Task.FromResult<IReadOnlyList<CardCommentRecord>>(_comments.Values.Where(row => row.OrganizationId == organization && row.CardId == card
            && (beforeCreatedAt is null || row.CreatedAt < beforeCreatedAt || row.CreatedAt == beforeCreatedAt && string.CompareOrdinal(row.Id.ToString("N"), beforeId!.Value.ToString("N")) < 0))
            .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id.ToString("N"), StringComparer.Ordinal).Take(51).ToArray());
    }
    public Task<CardCommentRecord?> EditAsync(Guid organization, Guid card, Guid comment, Guid author, long version, string content, DateTimeOffset at, CancellationToken ct)
        => ChangeComment(organization, card, comment, author, version, CardComment.RequireContent(content), at, false, ct);
    public Task<CardCommentRecord?> DeleteAsync(Guid organization, Guid card, Guid comment, Guid author, long version, DateTimeOffset at, CancellationToken ct)
        => ChangeComment(organization, card, comment, author, version, null, at, true, ct);
    private Task<CardCommentRecord?> ChangeComment(Guid organization, Guid card, Guid comment, Guid author, long version, string? content, DateTimeOffset at, bool deleting, CancellationToken ct)
    {
        RequireCommentScope(organization); ct.ThrowIfCancellationRequested(); var now = AttachmentMetadataMapping.DatabaseTimestamp(at);
        lock (_sync)
        {
            if (!_comments.TryGetValue(comment, out var row) || row.OrganizationId != organization || row.CardId != card || row.AuthorId != author
                || row.Version != version || version == long.MaxValue || row.DeletedAt is not null || now < row.UpdatedAt || !deleting && row.Content == content)
                return Task.FromResult<CardCommentRecord?>(null);
            var updated = row with { Content = deleting ? null : content, UpdatedAt = now, Version = version + 1,
                EditedAt = deleting ? row.EditedAt : now, DeletedAt = deleting ? now : null, DeletedBy = deleting ? author : null };
            _comments[comment] = updated; return Task.FromResult<CardCommentRecord?>(updated);
        }
    }
}
