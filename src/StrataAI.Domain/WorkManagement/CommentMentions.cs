namespace StrataAI.Domain.WorkManagement;

public static class MentionHandle
{
    public const int MaximumLength = 40;
    public const int MaximumLifetimeReservations = 32;
    public static string Normalize(string value)
    {
        if (value is null || value.Length > MaximumLength) throw new ArgumentException("Mention handle is invalid.");
        var handle = value.Trim().ToLowerInvariant();
        if (handle.Length is < 3 or > MaximumLength || !Letter(handle[0]) || handle.Any(c => !Character(c)) || handle is "card" or "board")
            throw new ArgumentException("Mention handle is invalid.");
        return handle;
    }
    public static string RequireCustom(string value)
    {
        var handle = Normalize(value);
        if (handle.StartsWith("u_", StringComparison.Ordinal)) throw new ArgumentException("Mention handle namespace is reserved.");
        return handle;
    }
    public static string DefaultForUser(Guid user)
        => user == Guid.Empty ? throw new ArgumentException("Mention account is required.") : $"u_{user:N}";
    internal static bool Letter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    internal static bool Character(char c) => Letter(c) || c is >= '0' and <= '9' or '_';
}
public enum CommentMentionKind { User, Card, Board }
public sealed record CommentMentionToken(CommentMentionKind Kind, string Handle, int Start, int Length);
public sealed record BoundCommentUserMention(Guid UserId, string Handle, int Start, int Length);

// Lexical references are declarations, never authorization or recipient intent.
// Application must resolve current scoped handles and separately admit/rate-limit
// mass mentions in the owning comment transaction before producing notifications.
public sealed class CommentMentionText
{
    public const int MaximumTokens = 64;
    public const int MaximumUserRecipients = 20;
    private CommentMentionText(string content, IReadOnlyList<CommentMentionToken> tokens) { Content = content; Tokens = tokens; }
    public string Content { get; }
    // Offsets count UTF-16 units in the normalized persisted body, not raw input.
    public IReadOnlyList<CommentMentionToken> Tokens { get; }
    public static CommentMentionText Parse(string content)
    {
        var normalized = CardComment.RequireContent(content); var tokens = new List<CommentMentionToken>(); var segmentStart = 0;
        for (var at = 0; at < normalized.Length; at++)
        {
            if (char.IsWhiteSpace(normalized[at])) { segmentStart = at + 1; continue; }
            if (normalized[at] != '@' || at > 0 && !Boundary(normalized[at - 1])) continue;
            var prefix = normalized.AsSpan(segmentStart, at - segmentStart);
            if (prefix.Contains("://", StringComparison.Ordinal) || prefix.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) continue;
            var end = at + 1;
            while (end < normalized.Length && MentionHandle.Character(normalized[end])) end++;
            if (end == at + 1 || end < normalized.Length && (normalized[end] == '@' || char.IsLetterOrDigit(normalized[end])
                || char.IsSurrogate(normalized[end]) || normalized[end] is '.' or '-' && end + 1 < normalized.Length && MentionHandle.Character(normalized[end + 1]))) continue;
            var handle = normalized[(at + 1)..end].ToLowerInvariant();
            var kind = handle switch { "card" => CommentMentionKind.Card, "board" => CommentMentionKind.Board, _ => CommentMentionKind.User };
            if (kind == CommentMentionKind.User)
            {
                try { handle = MentionHandle.Normalize(handle); }
                catch (ArgumentException) { continue; }
            }
            if (tokens.Count == MaximumTokens) throw new ArgumentException("Too many comment mention tokens.");
            tokens.Add(new(kind, handle, at, end - at)); at = end - 1;
        }
        return new(normalized, tokens.AsReadOnly());
    }
    public IReadOnlyList<BoundCommentUserMention> BindUsers(IReadOnlyDictionary<string, Guid> authorizedHandles)
    {
        ArgumentNullException.ThrowIfNull(authorizedHandles);
        if (authorizedHandles.Count > MaximumTokens) throw new ArgumentException("Authorized mention handle window is too large.");
        foreach (var entry in authorizedHandles)
            if (entry.Value == Guid.Empty || MentionHandle.Normalize(entry.Key) != entry.Key) throw new ArgumentException("Authorized mention handles must be canonical.");
        var references = new List<BoundCommentUserMention>(); var recipients = new HashSet<Guid>();
        foreach (var token in Tokens)
        {
            if (token.Kind != CommentMentionKind.User || !authorizedHandles.TryGetValue(token.Handle, out var user)) continue;
            if (recipients.Add(user) && recipients.Count > MaximumUserRecipients) throw new ArgumentException("Too many comment mention recipients.");
            references.Add(new(user, token.Handle, token.Start, token.Length));
        }
        return references.AsReadOnly();
    }
    private static bool Boundary(char c) => char.IsWhiteSpace(c) || c is '(' or '[' or '{' or ',' or ';' or ':' or '!' or '?';
}
