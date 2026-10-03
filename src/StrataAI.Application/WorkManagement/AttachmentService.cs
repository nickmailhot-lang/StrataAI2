using System.Globalization;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record CreateUrlAttachmentInput(string? Title, string? Url, long CardVersion);
public sealed record AttachmentPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    IReadOnlyList<AttachmentMetadata> Items, string? NextCursor, bool CanEdit);
public sealed record AttachmentChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion, AttachmentMetadata Attachment);

public sealed class AttachmentService(IWorkManagementStore work, IAttachmentMetadataStore attachments,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions,
    IWorkCommandContext context, ICommandActorAuthorization actors, IClock clock, IWorkEventStore events)
{
    public async Task<WorkOperation<AttachmentPage>> ListAsync(Guid cardId, Guid actor, string? after, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentPage>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found", () => Admit(hint, actor, false, ct), async () =>
        {
            DateTimeOffset? created = null; Guid? id = null;
            if (after is not null)
            {
                var parts = after.Length <= 128 ? after.Split('/') : [];
                if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out var parent) || parent != cardId
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                    || ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks || ticks % 10 != 0
                    || !Guid.TryParseExact(parts[2], "D", out var parsed) || parsed == Guid.Empty)
                    return WorkOperation<AttachmentPage>.Failure("invalid_attachment_cursor");
                created = new(ticks, TimeSpan.Zero); id = parsed;
            }
            var current = await work.FindCardAsync(cardId, ct);
            var rows = await attachments.ListAttachmentsAsync(hint.OrganizationId, cardId, created, id, ct);
            if (current is null || !await Admit(hint, actor, false, ct)) return WorkOperation<AttachmentPage>.Failure("card_not_found");
            var items = rows.Take(50).ToArray();
            var cursor = rows.Count > 50 ? $"{cardId:D}/{items[^1].CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}/{items[^1].Id:D}" : null;
            return WorkOperation<AttachmentPage>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, items, cursor, await Admit(hint, actor, true, ct)));
        }, ct);
    }
    public async Task<WorkOperation<AttachmentChange>> CreateUrlAsync(Guid cardId, Guid actor, CreateUrlAttachmentInput input,
        string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<AttachmentChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "AttachmentUrlCreate", cardId, input, "card_not_found"),
            async receipt => (receipt is null || receipt.OrganizationId == hint.OrganizationId && receipt.BoardId == hint.BoardId
                && receipt.CardId == cardId && receipt.Attachment.OrganizationId == hint.OrganizationId && receipt.Attachment.CardId == cardId)
                && await Admit(hint, actor, true, ct)
                && (receipt is null || await attachments.FindAttachmentAsync(hint.OrganizationId, cardId, receipt.Attachment.Id, ct) is not null), async () =>
            {
                if (input.CardVersion < 1) return WorkOperation<AttachmentChange>.Failure("invalid_attachment_version");
                var now = clock.UtcNow;
                StrataAI.Domain.WorkManagement.Attachment value;
                try { value = StrataAI.Domain.WorkManagement.Attachment.AttachUrl(Guid.NewGuid(), hint.OrganizationId, cardId, actor, input.Title!, input.Url!, now); }
                catch (ArgumentException) { return WorkOperation<AttachmentChange>.Failure("invalid_attachment_url"); }
                var current = await work.FindCardAsync(cardId, ct);
                if (current is null || current.Version != input.CardVersion) return WorkOperation<AttachmentChange>.Failure("version_conflict");
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, input.CardVersion, now, ct);
                if (updated is null) return WorkOperation<AttachmentChange>.Failure("version_conflict");
                var attachment = await attachments.CreateUrlAttachmentAsync(value.Id, hint.OrganizationId, cardId, actor, value.DisplayName, value.Url!, now, ct);
                await work.AppendAuditAsync(hint.OrganizationId, actor, "ATTACHMENT_ADDED", "Attachment", attachment.Id, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, "ATTACHMENT_ADDED", "Card", cardId, updated.Version, correlationId, now), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<AttachmentChange>.Failure("session_unavailable");
                return WorkOperation<AttachmentChange>.Success(new(hint.OrganizationId, hint.BoardId, cardId, updated.Version, attachment));
            }, ct);
    }
    private Task<bool> Admit(CardRecord hint, Guid actor, bool editing, CancellationToken ct) =>
        AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, editing, ct);
}
