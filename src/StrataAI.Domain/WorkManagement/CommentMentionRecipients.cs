namespace StrataAI.Domain.WorkManagement;

// Immutable inputs/outputs for the owning producer. The mapping must come from
// current authorized Board resolution; this class does not establish admission.
public sealed class CommentMentionRecipients
{
    private CommentMentionRecipients(IReadOnlyList<BoundCommentUserMention> references,
        IReadOnlyList<Guid> current, IReadOnlyList<Guid> added)
    { References = references; Current = current; Added = added; }
    public IReadOnlyList<BoundCommentUserMention> References { get; }
    // Persist Current with this comment revision, including self references.
    // Added is the notification delta; compare IDs, never mutable handles/names.
    public IReadOnlyList<Guid> Current { get; }
    public IReadOnlyList<Guid> Added { get; }
    public static CommentMentionRecipients Capture(CommentMentionText text, Guid actor,
        IReadOnlyDictionary<string, Guid> authorizedCurrentHandles, IReadOnlyList<Guid> previous)
        => CaptureConfirmedGroups(text, actor, authorizedCurrentHandles, previous, false, false, [], []);

    // Group inputs must be complete, currently authorized rosters. Confirmation
    // validates intent only; authorization and the durable quota remain the
    // owning Application producer's responsibility.
    public static CommentMentionRecipients CaptureConfirmedGroups(CommentMentionText text, Guid actor,
        IReadOnlyDictionary<string, Guid> authorizedCurrentHandles, IReadOnlyList<Guid> previous,
        bool cardConfirmed, bool boardConfirmed, IReadOnlyList<Guid> cardRecipients, IReadOnlyList<Guid> boardRecipients)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(cardRecipients); ArgumentNullException.ThrowIfNull(boardRecipients);
        if (actor == Guid.Empty
            || previous.Any(id => id == Guid.Empty) || previous.Distinct().Count() != previous.Count)
            throw new ArgumentException("Mention recipient history is invalid.");
        ValidateGroup(CommentMentionKind.Card, cardConfirmed, cardRecipients);
        ValidateGroup(CommentMentionKind.Board, boardConfirmed, boardRecipients);
        var references = text.BindUsers(authorizedCurrentHandles).ToArray();
        var current = references.Select(item => item.UserId).Concat(cardRecipients).Concat(boardRecipients).Distinct().Order().ToArray();
        var prior = previous.ToHashSet();
        var added = current.Where(id => id != actor && !prior.Contains(id)).ToArray();
        return new(Array.AsReadOnly(references), Array.AsReadOnly(current), Array.AsReadOnly(added));

        void ValidateGroup(CommentMentionKind kind, bool confirmed, IReadOnlyList<Guid> recipients)
        {
            if (confirmed && !text.Tokens.Any(token => token.Kind == kind)
                || !confirmed && recipients.Count != 0
                || recipients.Any(id => id == Guid.Empty) || recipients.Distinct().Count() != recipients.Count)
                throw new ArgumentException("Confirmed mention group is invalid.");
        }
    }
}
