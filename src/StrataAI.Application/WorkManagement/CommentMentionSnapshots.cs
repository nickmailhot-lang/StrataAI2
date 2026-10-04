namespace StrataAI.Application.WorkManagement;

public sealed class CommentMentionSnapshot
{
    public CommentMentionSnapshot(Guid organization, Guid card, Guid comment, long version, DateTimeOffset at, IReadOnlyList<Guid> recipients)
    {
        RequireIdentity(organization, card, comment, version);
        ArgumentNullException.ThrowIfNull(recipients);
        if (at.Offset != TimeSpan.Zero || at.UtcTicks % 10 != 0
            || recipients.Any(id => id == Guid.Empty) || recipients.Distinct().Count() != recipients.Count)
            throw new ArgumentException("Mention snapshot metadata is invalid.");
        OrganizationId = organization; CardId = card; CommentId = comment; CommentVersion = version; CreatedAt = at;
        Recipients = Array.AsReadOnly(recipients.Order().ToArray());
    }
    public Guid OrganizationId { get; }
    public Guid CardId { get; }
    public Guid CommentId { get; }
    public long CommentVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<Guid> Recipients { get; }
    public static void RequireIdentity(Guid organization, Guid card, Guid comment, long version)
    {
        if (organization == Guid.Empty || card == Guid.Empty || comment == Guid.Empty || version < 1)
            throw new ArgumentException("Mention snapshot identity is invalid.");
    }
    public bool SameAs(CommentMentionSnapshot other) => OrganizationId == other.OrganizationId && CardId == other.CardId
        && CommentId == other.CommentId && CommentVersion == other.CommentVersion && CreatedAt == other.CreatedAt
        && Recipients.SequenceEqual(other.Recipients);
}
// Internal metadata: caller admission/current recipient eligibility are producer
// responsibilities. Storage requires the owning authorized Work transaction.
public interface ICommentMentionSnapshotStore
{
    Task<CommentMentionSnapshot?> FindSnapshotAsync(Guid organization, Guid card, Guid comment, long version, CancellationToken ct = default);
    Task AppendSnapshotAsync(CommentMentionSnapshot snapshot, CancellationToken ct = default);
}
