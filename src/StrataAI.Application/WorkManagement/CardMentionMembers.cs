using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record CardMentionMember(Guid UserId, string Handle, string DisplayName, long HandleVersion);
// Current Board participant metadata, never an arbitrary global account lookup.
// Caller admission and final recipient eligibility are Application obligations.
public interface ICardMentionMemberStore
{
    Task<IReadOnlyList<CardMentionMember>> SearchAsync(Guid organization, Guid board, string prefix, string? after,
        bool requireVerifiedEmail, CancellationToken ct = default);
    Task<IReadOnlyList<CardMentionMember>> ResolveAsync(Guid organization, Guid board, IReadOnlyList<string> handles,
        bool requireVerifiedEmail, CancellationToken ct = default);
    // Producer-only current target locking, bounded to the direct username
    // recipient window. This does not establish caller/Card admission.
    Task<IReadOnlyList<CardMentionMember>> LockRecipientsAsync(Guid organization, Guid board, IReadOnlyList<string> handles,
        bool requireVerifiedEmail, CancellationToken ct = default);
}
public static class CardMentionLookup
{
    public const int PageSize = 20;
    public static string Prefix(string value)
    {
        if (value is null || value.Length > MentionHandle.MaximumLength) throw new ArgumentException("Mention prefix is invalid.");
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 0 && normalized[0] is not (>= 'a' and <= 'z')
            || normalized.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new ArgumentException("Mention prefix is invalid.");
        return normalized;
    }
    public static void Search(string prefix, string? after)
    {
        if (Prefix(prefix) != prefix || after is not null && (MentionHandle.Normalize(after) != after || !after.StartsWith(prefix, StringComparison.Ordinal)))
            throw new ArgumentException("Mention cursor is invalid.");
    }
    public static string[] Targets(IReadOnlyList<string> handles)
    {
        if (handles is null || handles.Count > CommentMentionText.MaximumTokens) throw new ArgumentException("Mention targets are invalid.");
        foreach (var handle in handles) if (MentionHandle.Normalize(handle) != handle) throw new ArgumentException("Mention targets are invalid.");
        return handles.Distinct(StringComparer.Ordinal).ToArray();
    }
}
