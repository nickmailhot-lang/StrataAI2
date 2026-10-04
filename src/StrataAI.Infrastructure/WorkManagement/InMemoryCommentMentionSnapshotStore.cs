using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : ICommentMentionSnapshotStore
{
    private readonly Dictionary<(Guid Organization, Guid Comment, long Version), CommentMentionSnapshot> _mentionSnapshots = [];
    public Task<CommentMentionSnapshot?> FindSnapshotAsync(Guid organization, Guid card, Guid comment, long version, CancellationToken ct = default)
    {
        RequireCommentScope(organization); CommentMentionSnapshot.RequireIdentity(organization, card, comment, version); ct.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_mentionSnapshots.TryGetValue((organization, comment, version), out var row) && row.CardId == card ? row : null);
    }
    public async Task AppendSnapshotAsync(CommentMentionSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); RequireCommentScope(snapshot.OrganizationId); ct.ThrowIfCancellationRequested();
        var key = (snapshot.OrganizationId, snapshot.CommentId, snapshot.CommentVersion);
        lock (_sync)
            if (_mentionSnapshots.TryGetValue(key, out var previous))
            {
                if (!previous.SameAs(snapshot)) throw new InvalidOperationException("Mention snapshot revision was reused.");
                return;
            }
        foreach (var recipient in snapshot.Recipients)
            if (await organizations.FindMembershipAsync(snapshot.OrganizationId, recipient, ct) is null)
                throw new InvalidOperationException("Mention snapshot recipient Organization is unavailable.");
        lock (_sync)
        {
            if (!_comments.TryGetValue(snapshot.CommentId, out var comment) || comment.OrganizationId != snapshot.OrganizationId
                || comment.CardId != snapshot.CardId || comment.Version != snapshot.CommentVersion || comment.UpdatedAt != snapshot.CreatedAt
                || comment.DeletedAt is not null && snapshot.Recipients.Count != 0)
                throw new InvalidOperationException("Current comment revision is unavailable.");
            ct.ThrowIfCancellationRequested(); _mentionSnapshots.Add(key, snapshot);
        }
    }
}
