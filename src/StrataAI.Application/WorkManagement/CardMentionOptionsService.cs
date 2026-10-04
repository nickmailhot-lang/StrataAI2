using System.Globalization;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record CardMentionOptionsPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    string Prefix, IReadOnlyList<CardMentionMember> Items, string? NextCursor);

// Discovery is admitted as a current COMMENT capability. Options are hints,
// never an authorization receipt for a later notification command.
public sealed class CardMentionOptionsService(IWorkManagementStore work, ICardMentionMemberStore members,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions,
    IdentityPolicy policy)
{
    public async Task<WorkOperation<CardMentionOptionsPage>> ListAsync(Guid cardId, Guid actor, string prefix,
        string? after, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardMentionOptionsPage>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found", () => Admit(hint, actor, ct), async () =>
        {
            string normalized;
            try { normalized = CardMentionLookup.Prefix(prefix); }
            catch (ArgumentException) { return WorkOperation<CardMentionOptionsPage>.Failure("mention_prefix_invalid"); }
            var current = await work.FindCardAsync(cardId, ct);
            if (current is null) return WorkOperation<CardMentionOptionsPage>.Failure("card_not_found");
            string? anchor = null;
            if (after is not null)
            {
                var parts = after.Length <= 160 ? after.Split('/') : [];
                if (parts.Length != 4 || parts[0] != cardId.ToString("D") || parts[2] != normalized
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                    || version < 1 || parts[1] != version.ToString(CultureInfo.InvariantCulture))
                    return WorkOperation<CardMentionOptionsPage>.Failure("invalid_mention_cursor");
                try { CardMentionLookup.Search(normalized, parts[3]); }
                catch (ArgumentException) { return WorkOperation<CardMentionOptionsPage>.Failure("invalid_mention_cursor"); }
                if (version != current.Version) return WorkOperation<CardMentionOptionsPage>.Failure("version_conflict");
                anchor = parts[3];
            }
            var rows = await members.SearchAsync(hint.OrganizationId, hint.BoardId, normalized, anchor, policy.RequireVerifiedEmail, ct);
            if (!await Admit(hint, actor, ct)) return WorkOperation<CardMentionOptionsPage>.Failure("card_not_found");
            var items = rows.Take(CardMentionLookup.PageSize).ToArray();
            var cursor = rows.Count > CardMentionLookup.PageSize
                ? $"{cardId:D}/{current.Version.ToString(CultureInfo.InvariantCulture)}/{normalized}/{items[^1].Handle}" : null;
            return WorkOperation<CardMentionOptionsPage>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, normalized, items, cursor));
        }, ct);
    }
    private async Task<bool> Admit(CardRecord hint, Guid actor, CancellationToken ct)
        => actor != Guid.Empty && await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, true, ct)
            && (await work.FindBoardMemberAsync(hint.BoardId, actor, ct)) is { Active: true };
}
