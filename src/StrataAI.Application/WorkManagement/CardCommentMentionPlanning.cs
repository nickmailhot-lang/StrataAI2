using StrataAI.Application.Identity;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record CardCommentMentionPlan(string Content, CommentMentionRecipients Recipients, bool HasCardMention, bool HasBoardMention);

// Internal producer preparation in its already authorized owning transaction.
// This is neither an HTTP capability nor a durable recipient/session receipt.
// Publication must revalidate current targets and atomically retain the snapshot.
public sealed class CardCommentMentionPlanning(ICardMentionMemberStore members, IdentityPolicy policy)
{
    public async Task<WorkOperation<CardCommentMentionPlan>> ResolveAsync(Guid organization, Guid board, Guid actor,
        string content, IReadOnlyList<Guid> previous, CancellationToken ct = default)
    {
        CommentMentionText parsed;
        try { parsed = CommentMentionText.Parse(content); }
        catch (ArgumentException) { return WorkOperation<CardCommentMentionPlan>.Failure("invalid_comment_mentions"); }
        var targets = parsed.Tokens.Where(token => token.Kind == CommentMentionKind.User).Select(token => token.Handle).Distinct(StringComparer.Ordinal).ToArray();
        var current = await members.ResolveAsync(organization, board, targets, policy.RequireVerifiedEmail, ct);
        // The store returns current eligible participants only. Unknown/former
        // aliases remain literal rather than being rebound to global metadata.
        CommentMentionRecipients recipients;
        try { recipients = CommentMentionRecipients.Capture(parsed, actor, current.ToDictionary(row => row.Handle, row => row.UserId, StringComparer.Ordinal), previous); }
        catch (ArgumentException) { return WorkOperation<CardCommentMentionPlan>.Failure("invalid_comment_mentions"); }
        ct.ThrowIfCancellationRequested();
        return WorkOperation<CardCommentMentionPlan>.Success(new(parsed.Content, recipients,
            parsed.Tokens.Any(token => token.Kind == CommentMentionKind.Card), parsed.Tokens.Any(token => token.Kind == CommentMentionKind.Board)));
    }
}
