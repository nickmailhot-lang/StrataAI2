namespace StrataAI.Application.Identity;

public sealed record UserMentionHandle(Guid UserId, string Handle, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, long Version);
public sealed record UserMentionHandleChange(UserMentionHandle Current, bool Changed);

// Global account storage only. Current-session ownership, receipts, identity
// events/audit and Board-scoped recipient resolution belong to Application.
public interface IUserMentionHandleStore
{
    Task<UserMentionHandle?> FindAsync(Guid userId, CancellationToken cancellationToken);
    Task<IdentityOperation<UserMentionHandleChange>> ClaimAsync(Guid userId, string handle,
        long expectedVersion, DateTimeOffset updatedAt, CancellationToken cancellationToken);
}
