namespace StrataAI.Application.WorkManagement;

// Internal fan-out only. The originating mutation owns the Board gate and must
// already validate the post-mutation Card scope. Never expose this as a roster.
public interface ICardWatchRecipientStore
{
    Task<IReadOnlyList<Guid>> LockRecipientsAsync(CardWatchActivity scope, bool requireVerifiedEmail, CancellationToken ct);
}
