using StrataAI.Application.Identity;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record CardCommentMentionPlan(string Content, CommentMentionRecipients Recipients, bool HasCardMention, bool HasBoardMention,
    IReadOnlyList<CardMentionMember> CurrentMembers);

// Internal producer preparation in its already authorized owning transaction.
// This is neither an HTTP capability nor a durable recipient/session receipt.
// Publication must revalidate current targets and atomically retain the snapshot.
public sealed class CardCommentMentionPlanning(ICardMentionMemberStore members, IdentityPolicy policy)
{
    public WorkOperation<bool> ValidateSelections(CardCommentMentionPlan plan, IReadOnlyList<CardCommentMentionSelection>? selected)
    {
        if (selected is null || selected.Count == 0) return WorkOperation<bool>.Success(true);
        if (selected.Count > CommentMentionText.MaximumUserRecipients || selected.Any(row => row is null
            || row.UserId == Guid.Empty || row.HandleVersion < 1 || row.Handle is null)
            || selected.Select(row => row.UserId).Distinct().Count() != selected.Count
            || selected.Select(row => row.Handle).Distinct(StringComparer.Ordinal).Count() != selected.Count)
            return WorkOperation<bool>.Failure("invalid_comment_mentions");
        var declared = CommentMentionText.Parse(plan.Content).Tokens.Where(row => row.Kind == CommentMentionKind.User).Select(row => row.Handle).ToHashSet(StringComparer.Ordinal);
        foreach (var row in selected)
        {
            try { if (MentionHandle.Normalize(row.Handle) != row.Handle || !declared.Contains(row.Handle)) return WorkOperation<bool>.Failure("invalid_comment_mentions"); }
            catch (ArgumentException) { return WorkOperation<bool>.Failure("invalid_comment_mentions"); }
            if (!plan.CurrentMembers.Any(current => current.UserId == row.UserId && current.Handle == row.Handle && current.HandleVersion == row.HandleVersion))
                return WorkOperation<bool>.Failure("mention_targets_changed");
        }
        return WorkOperation<bool>.Success(true);
    }
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
            parsed.Tokens.Any(token => token.Kind == CommentMentionKind.Card), parsed.Tokens.Any(token => token.Kind == CommentMentionKind.Board),
            Array.AsReadOnly(current.ToArray())));
    }
    public async Task<WorkOperation<bool>> RevalidateAsync(Guid organization, Guid board, CardCommentMentionPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan); ct.ThrowIfCancellationRequested();
        var expected = plan.CurrentMembers;
        if (expected.Count > CommentMentionText.MaximumUserRecipients) return WorkOperation<bool>.Failure("mention_targets_changed");
        var current = await members.LockRecipientsAsync(organization, board, expected.Select(row => row.Handle).ToArray(), policy.RequireVerifiedEmail, ct);
        if (current.Count != expected.Count || expected.Any(row => !current.Any(now => now.UserId == row.UserId
            && now.Handle == row.Handle && now.HandleVersion == row.HandleVersion)))
            return WorkOperation<bool>.Failure("mention_targets_changed");
        ct.ThrowIfCancellationRequested(); return WorkOperation<bool>.Success(true);
    }
}
