namespace StrataAI.Application.WorkManagement;

// Internal full group roster, never an HTTP directory or permission capability.
// The owning producer must admit caller/parents, confirm and rate-limit mass
// intent, retain full revision history and atomically publish the resulting delta.
public interface ICardMassMentionMemberStore
{
    Task<IReadOnlyList<Guid>> LockRecipientsAsync(Guid organization, Guid board, Guid card,
        bool includeCard, bool includeBoard, bool requireVerifiedEmail, CancellationToken ct = default);
}
