using StrataAI.Domain.Common;

namespace StrataAI.Domain.WorkManagement;

// Author ownership is necessary, not sufficient: Application commands must
// also admit the current authenticated actor and current Card/Board parents.
// Card identity remains canonical when a Card moves between Boards.
public sealed class CardComment : DomainEntity, IOrganizationScoped
{
    public const int MaximumContentLength = 10000;

    public CardComment(Guid id, Guid organizationId, Guid cardId, Guid authorId, string content, DateTimeOffset at)
        : base(id, at.ToUniversalTime())
    {
        if (organizationId == Guid.Empty || cardId == Guid.Empty || authorId == Guid.Empty)
            throw new ArgumentException("Comment scope and author are required.");
        OrganizationId = organizationId; CardId = cardId; AuthorId = authorId;
        Content = RequireContent(content);
    }
    public Guid OrganizationId { get; }
    public Guid CardId { get; }
    public Guid AuthorId { get; }
    public string? Content { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public bool IsEdited => EditedAt is not null;
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    public bool Edit(Guid actor, long expectedVersion, string content, DateTimeOffset at)
    {
        RequireAuthor(actor); RequireRevision(expectedVersion, at);
        if (DeletedAt is not null) throw new InvalidOperationException("Comment is deleted.");
        var value = RequireContent(content);
        if (value == Content) return false;
        RequireNextVersion(); MarkUpdated(at.ToUniversalTime()); Content = value; EditedAt = UpdatedAt;
        return true;
    }
    public bool Delete(Guid actor, long expectedVersion, DateTimeOffset at)
    {
        RequireAuthor(actor); RequireRevision(expectedVersion, at);
        if (DeletedAt is not null) return false;
        RequireNextVersion(); MarkUpdated(at.ToUniversalTime());
        Content = null; DeletedAt = UpdatedAt; DeletedBy = actor;
        return true;
    }
    private void RequireAuthor(Guid actor)
    {
        if (actor == Guid.Empty || actor != AuthorId) throw new UnauthorizedAccessException("Comment author is required.");
    }
    private void RequireRevision(long expectedVersion, DateTimeOffset at)
    {
        if (expectedVersion < 1 || expectedVersion != Version) throw new InvalidOperationException("Comment revision changed.");
        if (at.ToUniversalTime() < UpdatedAt) throw new ArgumentOutOfRangeException(nameof(at), "Comment timestamp cannot move backwards.");
    }
    private void RequireNextVersion()
    {
        if (Version == long.MaxValue) throw new InvalidOperationException("Comment revision is exhausted.");
    }
    public static string RequireContent(string content)
    {
        // Plain text only. The UI must render text, never execute markup or
        // resolve URLs/mentions solely because arbitrary body text resembles one.
        if (content is null || content.Length > MaximumContentLength) throw new ArgumentException("Comment content is invalid.", nameof(content));
        for (var index = 0; index < content.Length; index++)
        {
            var value = content[index];
            if (char.IsControl(value) && value is not ('\r' or '\n' or '\t'))
                throw new ArgumentException("Comment content is invalid.", nameof(content));
            if (char.IsHighSurrogate(value))
            {
                if (index + 1 >= content.Length || !char.IsLowSurrogate(content[++index]))
                    throw new ArgumentException("Comment content is invalid.", nameof(content));
            }
            else if (char.IsLowSurrogate(value)) throw new ArgumentException("Comment content is invalid.", nameof(content));
        }
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length == 0) throw new ArgumentException("Comment content is required.", nameof(content));
        return normalized;
    }
}
