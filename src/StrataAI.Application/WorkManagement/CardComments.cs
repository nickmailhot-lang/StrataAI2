namespace StrataAI.Application.WorkManagement;

public sealed record CardCommentRecord(Guid Id, Guid OrganizationId, Guid CardId, Guid AuthorId, string? Content,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version, DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt, Guid? DeletedBy);

// All methods require an owning, currently authorized Application scope. A
// tenant ID and author predicate alone do not establish current Board rights.
public interface ICardCommentStore
{
    Task<CardCommentRecord> CreateAsync(Guid id, Guid organization, Guid card, Guid author, string content, DateTimeOffset at, CancellationToken ct);
    Task<CardCommentRecord?> FindAsync(Guid organization, Guid card, Guid comment, CancellationToken ct);
    Task<IReadOnlyList<CardCommentRecord>> ListAsync(Guid organization, Guid card, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct);
    // Equal-content/no-op and exact original receipt recovery belong to the
    // Application command; NULL means no actual transition won this CAS.
    Task<CardCommentRecord?> EditAsync(Guid organization, Guid card, Guid comment, Guid author, long version, string content, DateTimeOffset at, CancellationToken ct);
    Task<CardCommentRecord?> DeleteAsync(Guid organization, Guid card, Guid comment, Guid author, long version, DateTimeOffset at, CancellationToken ct);
}
