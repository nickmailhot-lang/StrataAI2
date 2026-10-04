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
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(previous);
        if (actor == Guid.Empty
            || previous.Any(id => id == Guid.Empty) || previous.Distinct().Count() != previous.Count)
            throw new ArgumentException("Mention recipient history is invalid.");
        var references = text.BindUsers(authorizedCurrentHandles).ToArray();
        var current = references.Select(item => item.UserId).Distinct().Order().ToArray();
        var prior = previous.ToHashSet();
        var added = current.Where(id => id != actor && !prior.Contains(id)).ToArray();
        return new(Array.AsReadOnly(references), Array.AsReadOnly(current), Array.AsReadOnly(added));
    }
}
