using System.Globalization;
using System.Text.Json.Serialization;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Application.WorkManagement;

public sealed record CardCommentMentionSelection(Guid UserId, string Handle, long HandleVersion);
public sealed record CreateCardCommentInput(string? Content, long CardVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CardCommentMentionSelection>? MentionSelections = null);
public sealed record EditCardCommentInput(string? Content, long CardVersion, long Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CardCommentMentionSelection>? MentionSelections = null);
public sealed record DeleteCardCommentInput(long CardVersion, long Version, bool Confirmed);
public sealed record CardCommentChange(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion, CardCommentRecord Comment, bool Changed);
public sealed record CardCommentPage(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
    IReadOnlyList<CardCommentRecord> Items, string? NextCursor, bool CanComment);

// Internal authenticated comment boundary. Public/Owner Portal projection needs
// its own explicit policy; visibility alone does not establish participation.
public sealed class CardCommentService(IWorkManagementStore work, ICardCommentStore comments,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions,
    IWorkCommandContext context, ICommandActorAuthorization actors, IClock clock, IWorkEventStore events,
    CardCommentMentionPlanning mentions, ICommentMentionSnapshotStore snapshots, IWorkNotificationStore notifications)
{
    // Durable recovery must never retain former comment plaintext. The body is
    // hydrated only from the currently admitted row at the recorded revision.
    public sealed record Receipt(Guid OrganizationId, Guid BoardId, Guid CardId, long CardVersion,
        Guid CommentId, long CommentVersion, Guid AuthorId, bool Changed);
    public async Task<WorkOperation<CardCommentPage>> ListAsync(Guid cardId, Guid actor, string? after, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardCommentPage>.Failure("card_not_found");
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "card_not_found", () => Admit(hint, actor, false, ct), async () =>
        {
            var current = await work.FindCardAsync(cardId, ct);
            if (current is null) return WorkOperation<CardCommentPage>.Failure("card_not_found");
            DateTimeOffset? before = null; Guid? beforeId = null;
            if (after is not null)
            {
                var parts = after.Length <= 160 ? after.Split('/') : [];
                if (parts.Length != 4 || !Guid.TryParseExact(parts[0], "D", out var parent) || parent != cardId
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
                    || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                    || ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks || ticks % 10 != 0
                    || !Guid.TryParseExact(parts[3], "D", out var id) || id == Guid.Empty)
                    return WorkOperation<CardCommentPage>.Failure("invalid_comment_cursor");
                if (current.Version != version) return WorkOperation<CardCommentPage>.Failure("version_conflict");
                before = new(ticks, TimeSpan.Zero); beforeId = id;
            }
            var rows = await comments.ListAsync(hint.OrganizationId, cardId, before, beforeId, ct);
            if (!await Admit(hint, actor, false, ct)) return WorkOperation<CardCommentPage>.Failure("card_not_found");
            var items = rows.Take(50).ToArray();
            var cursor = rows.Count > 50 ? $"{cardId:D}/{current.Version.ToString(CultureInfo.InvariantCulture)}/{items[^1].CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}/{items[^1].Id:D}" : null;
            return WorkOperation<CardCommentPage>.Success(new(hint.OrganizationId, hint.BoardId, cardId, current.Version, items, cursor,
                await Admit(hint, actor, true, ct)));
        }, ct);
    }

    public Task<WorkOperation<CardCommentChange>> CreateAsync(Guid cardId, Guid actor, CreateCardCommentInput input, string correlationId, CancellationToken ct = default)
        => Change(cardId, null, actor, input.CardVersion, null, input.Content, false, false, input.MentionSelections, input, correlationId, ct);
    public Task<WorkOperation<CardCommentChange>> EditAsync(Guid cardId, Guid commentId, Guid actor, EditCardCommentInput input, string correlationId, CancellationToken ct = default)
        => Change(cardId, commentId, actor, input.CardVersion, input.Version, input.Content, false, false, input.MentionSelections, input, correlationId, ct);
    public Task<WorkOperation<CardCommentChange>> DeleteAsync(Guid cardId, Guid commentId, Guid actor, DeleteCardCommentInput input, string correlationId, CancellationToken ct = default)
        => Change(cardId, commentId, actor, input.CardVersion, input.Version, null, true, input.Confirmed, null, input, correlationId, ct);

    private async Task<WorkOperation<CardCommentChange>> Change(Guid cardId, Guid? commentId, Guid actor, long cardVersion,
        long? commentVersion, string? content, bool deleting, bool confirmed, IReadOnlyList<CardCommentMentionSelection>? selected,
        object input, string correlationId, CancellationToken ct)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardCommentChange>.Failure("card_not_found");
        var type = deleting ? "COMMENT_DELETED" : commentId.HasValue ? "COMMENT_EDITED" : "COMMENT_ADDED";
        var result = await transactions.ExecuteAsync<Receipt>(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, type, commentId ?? cardId, new { cardId, input }, "comment_not_found"),
            async receipt =>
            {
                if (!await Admit(hint, actor, true, ct)) return false;
                if (commentId is { } requested && (await comments.FindAsync(hint.OrganizationId, cardId, requested, ct))?.AuthorId != actor) return false;
                if (receipt is null) return true;
                if (receipt.OrganizationId != hint.OrganizationId || receipt.BoardId != hint.BoardId || receipt.CardId != cardId
                    || receipt.AuthorId != actor || commentId is { } child && receipt.CommentId != child) return false;
                // Never replay a former body after an edit or redaction. A newer
                // unrelated Card version does not invalidate an exact receipt.
                var row = await comments.FindAsync(hint.OrganizationId, cardId, receipt.CommentId, ct);
                return row is not null && row.AuthorId == receipt.AuthorId && row.Version == receipt.CommentVersion
                    && await Admit(hint, actor, true, ct) && await actors.VerifyAsync(actor, ct);
            }, async () =>
            {
                if (cardVersion < 1 || commentId == Guid.Empty || commentId.HasValue && commentVersion is not > 0)
                    return WorkOperation<Receipt>.Failure("invalid_comment_version");
                if (deleting && !confirmed) return WorkOperation<Receipt>.Failure("comment_delete_confirmation_required");
                var current = await work.FindCardAsync(cardId, ct);
                var child = commentId is { } id ? await comments.FindAsync(hint.OrganizationId, cardId, id, ct) : null;
                if (current is null || commentId.HasValue && (child is null || child.AuthorId != actor)) return WorkOperation<Receipt>.Failure("comment_not_found");
                if (current.Version != cardVersion || child is not null && child.Version != commentVersion)
                    return WorkOperation<Receipt>.Failure("version_conflict");
                string? normalized = null;
                if (!deleting)
                {
                    if (child?.DeletedAt is not null) return WorkOperation<Receipt>.Failure("comment_not_found");
                    try { normalized = CardComment.RequireContent(content!); }
                    catch (ArgumentException) { return WorkOperation<Receipt>.Failure("invalid_comment_content"); }
                }
                if (child is not null && (deleting ? child.DeletedAt is not null : child.Content == normalized) && selected is not { Count: > 0 })
                    return await Complete(hint, actor, current.Version, child, false, ct);
                var previous = child is null ? null : await snapshots.FindSnapshotAsync(hint.OrganizationId, cardId, child.Id, child.Version, ct);
                CardCommentMentionPlan? plan = null;
                if (!deleting)
                {
                    var prepared = await mentions.ResolveAsync(hint.OrganizationId, hint.BoardId, actor, normalized!, previous?.Recipients ?? [], ct);
                    if (!prepared.Succeeded || prepared.Value is null) return WorkOperation<Receipt>.Failure(prepared.ErrorCode ?? "invalid_comment_mentions");
                    plan = prepared.Value;
                    var selection = mentions.ValidateSelections(plan, selected);
                    if (!selection.Succeeded) return WorkOperation<Receipt>.Failure(selection.ErrorCode!);
                    var targets = await mentions.RevalidateAsync(hint.OrganizationId, hint.BoardId, plan, ct);
                    if (!targets.Succeeded) return WorkOperation<Receipt>.Failure(targets.ErrorCode!);
                }
                if (child is not null && !deleting && child.Content == normalized)
                    return await Complete(hint, actor, current.Version, child, false, ct);
                var now = AttachmentMetadataMapping.DatabaseTimestamp(clock.UtcNow);
                if (current.Version == long.MaxValue || now < current.UpdatedAt || child is not null && (child.Version == long.MaxValue || now < child.UpdatedAt))
                    return WorkOperation<Receipt>.Failure("version_conflict");
                var updated = await work.UpdateCardAsync(cardId, current.Title, current.Description, cardVersion, now, ct);
                if (updated is null) return WorkOperation<Receipt>.Failure("version_conflict");
                var changed = child is null ? await comments.CreateAsync(Guid.NewGuid(), hint.OrganizationId, cardId, actor, normalized!, now, ct)
                    : deleting ? await comments.DeleteAsync(hint.OrganizationId, cardId, child.Id, actor, child.Version, now, ct)
                    : await comments.EditAsync(hint.OrganizationId, cardId, child.Id, actor, child.Version, normalized!, now, ct);
                if (changed is null) return WorkOperation<Receipt>.Failure("version_conflict");
                await snapshots.AppendSnapshotAsync(new(hint.OrganizationId, cardId, changed.Id, changed.Version, now,
                    plan?.Recipients.Current ?? []), ct);
                await work.AppendAuditAsync(hint.OrganizationId, actor, type, "Comment", changed.Id, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor, type, "Card", cardId, updated.Version, correlationId, now), ct);
                // One content-free source event for this revision's new stable
                // references, including self; only non-self deltas receive inbox items.
                if (plan is not null && plan.Recipients.Current.Except(previous?.Recipients ?? []).Any())
                {
                    var mentionEvent = new WorkEvent(Guid.NewGuid(), hint.OrganizationId, hint.BoardId, actor,
                        "MENTION_CREATED", "Card", cardId, updated.Version, correlationId, now);
                    await events.AppendAsync(mentionEvent, ct);
                    foreach (var recipient in plan.Recipients.Added)
                        await notifications.AppendCardMentionAsync(mentionEvent, recipient, ct);
                }
                if (plan is not null)
                {
                    var targets = await mentions.RevalidateAsync(hint.OrganizationId, hint.BoardId, plan, ct);
                    if (!targets.Succeeded) return WorkOperation<Receipt>.Failure(targets.ErrorCode!);
                }
                return await Complete(hint, actor, updated.Version, changed, true, ct);
            }, ct);
        if (!result.Succeeded || result.Value is null) return WorkOperation<CardCommentChange>.Failure(result.ErrorCode ?? "comment_not_found");
        var receipt = result.Value;
        return await transactions.ExecuteReadAsync(hint.OrganizationId, actor, "comment_not_found", () => Admit(hint, actor, true, ct), async () =>
        {
            var row = await comments.FindAsync(hint.OrganizationId, cardId, receipt.CommentId, ct);
            if (row is null || row.AuthorId != actor || row.Version != receipt.CommentVersion)
                return WorkOperation<CardCommentChange>.Failure("comment_not_found");
            return WorkOperation<CardCommentChange>.Success(new(receipt.OrganizationId, receipt.BoardId, cardId, receipt.CardVersion, row, receipt.Changed));
        }, ct);
    }
    private async Task<WorkOperation<Receipt>> Complete(CardRecord hint, Guid actor, long version, CardCommentRecord comment, bool changed, CancellationToken ct)
    {
        if (!await Admit(hint, actor, true, ct)) return WorkOperation<Receipt>.Failure("comment_not_found");
        if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<Receipt>.Failure("session_unavailable");
        return WorkOperation<Receipt>.Success(new(hint.OrganizationId, hint.BoardId, hint.Id, version, comment.Id, comment.Version, actor, changed));
    }
    private async Task<bool> Admit(CardRecord hint, Guid actor, bool writing, CancellationToken ct)
    {
        if (actor == Guid.Empty || !await AttachmentAdmission.CheckAsync(work, organizations, boards, hint, actor, writing, ct)) return false;
        // COMMENT requires explicit current Board participation; Organization
        // governance or PUBLIC visibility alone is insufficient.
        return !writing || (await work.FindBoardMemberAsync(hint.BoardId, actor, ct)) is { Active: true };
    }
}
