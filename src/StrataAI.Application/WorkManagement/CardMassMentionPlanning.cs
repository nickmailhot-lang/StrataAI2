using StrataAI.Application.Identity;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record CardCommentMassMentionConfirmation(bool Card, bool Board);
public sealed record CardMassMentionPlan(bool Card, bool Board, IReadOnlyList<Guid> CardRecipients, IReadOnlyList<Guid> BoardRecipients)
{
    public bool HasNewDelivery(IReadOnlyList<Guid> added)
        => added.Intersect(CardRecipients.Concat(BoardRecipients)).Any();
}
// Caller/group authorization is checked by the owning comment boundary. This
// plan captures full current rosters; repeat the reads before acknowledgment to
// refuse participants added during planning as well as departed recipients.
public sealed class CardMassMentionPlanning(ICardMassMentionMemberStore members, IdentityPolicy policy)
{
    public async Task<WorkOperation<CardMassMentionPlan>> ResolveAsync(Guid organization, Guid board, Guid card,
        string content, CardCommentMassMentionConfirmation confirmation, CancellationToken ct = default)
    {
        var text = CommentMentionText.Parse(content);
        if (confirmation.Card && !text.Tokens.Any(token => token.Kind == CommentMentionKind.Card)
            || confirmation.Board && !text.Tokens.Any(token => token.Kind == CommentMentionKind.Board))
            return WorkOperation<CardMassMentionPlan>.Failure("invalid_mass_mention_confirmation");
        var cardIds = confirmation.Card ? await members.LockRecipientsAsync(organization, board, card, true, false, policy.RequireVerifiedEmail, ct) : [];
        var boardIds = confirmation.Board ? await members.LockRecipientsAsync(organization, board, card, false, true, policy.RequireVerifiedEmail, ct) : [];
        return WorkOperation<CardMassMentionPlan>.Success(new(confirmation.Card, confirmation.Board,
            Array.AsReadOnly(cardIds.ToArray()), Array.AsReadOnly(boardIds.ToArray())));
    }
    public async Task<bool> RevalidateAsync(Guid organization, Guid board, Guid card, CardMassMentionPlan plan, CancellationToken ct = default)
    {
        var cardIds = plan.Card ? await members.LockRecipientsAsync(organization, board, card, true, false, policy.RequireVerifiedEmail, ct) : [];
        var boardIds = plan.Board ? await members.LockRecipientsAsync(organization, board, card, false, true, policy.RequireVerifiedEmail, ct) : [];
        return cardIds.SequenceEqual(plan.CardRecipients) && boardIds.SequenceEqual(plan.BoardRecipients);
    }
}
