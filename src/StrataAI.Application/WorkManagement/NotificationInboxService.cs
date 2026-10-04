using System.Globalization;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record NotificationCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public override string ToString() => $"{CreatedAt.ToUniversalTime():O}/{Id:D}";
    public static NotificationCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split('/');
        return parts.Length == 2 && DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var created) && created.Offset == TimeSpan.Zero &&
            Guid.TryParse(parts[1], out var id) && id != Guid.Empty ? new(created, id) : new(default, Guid.Empty);
    }
}

public sealed record NotificationInboxItem(Guid Id, Guid RecipientId, Guid ActorId, string Type,
    string EntityType, Guid EntityId, Guid BoardId, string EntityLink, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? CurrentBoardId = null);
public sealed record NotificationInboxPage(Guid OrganizationId, IReadOnlyList<NotificationInboxItem> Items, string? NextCursor);
public sealed record NotificationReadAcknowledgment(Guid Id, DateTimeOffset ReadAt);
public sealed record NotificationReadResult(Guid OrganizationId, IReadOnlyList<NotificationReadAcknowledgment> Items);

public interface INotificationInboxStore
{
    Task<IReadOnlyList<CardNotification>> ListVisibleAsync(Guid organizationId, Guid recipientId,
        NotificationCursor? after, bool requireVerifiedEmail, CancellationToken cancellationToken);
    Task<IReadOnlyList<CardNotification>> FindVisibleAsync(Guid organizationId, Guid recipientId,
        IReadOnlyCollection<Guid> ids, bool requireVerifiedEmail, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationReadAcknowledgment>> MarkReadAsync(Guid organizationId, Guid recipientId,
        IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class NotificationInboxService(INotificationInboxStore notifications, IWorkManagementStore work,
    IOrganizationStore organizations, IWorkBoardAuthorization boards, IWorkManagementUnitOfWork transactions,
    ICommandActorAuthorization actors, IWorkCommandContext context, IdentityPolicy policy, IClock clock,
    INotificationRealtimeStore journal)
{
    public sealed record SyncPage(Guid OrganizationId, Guid RecipientId, string Cursor, bool HasMore,
        IReadOnlyList<NotificationRealtimeEvent> Events);

    public Task<WorkOperation<SyncPage>> ReadEventsAsync(Guid organizationId, Guid recipientId, long after,
        CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || recipientId == Guid.Empty)
            return Task.FromResult(WorkOperation<SyncPage>.Failure("notification_not_found"));
        IReadOnlyList<NotificationRealtimeEvent> planned = [];
        IReadOnlyList<CardNotification> admitted = [];
        var more = false;
        return transactions.ExecuteAsync(organizationId,
            WorkCommand.Create(recipientId, null, "NotificationSync", organizationId, new { after }, "notification_not_found"),
            async _ => {
                if (!await AdmitOrganization(organizationId, recipientId, ct)) return false;
                if (after < 0) return true;
                var window = await journal.ListRecipientEventsAsync(organizationId, recipientId, after, ct);
                if (window.Count > 51 || window.Any(e => e.OrganizationId != organizationId || e.RecipientId != recipientId))
                    throw new InvalidOperationException("Invalid notification journal window.");
                var previous = after; var identities = new HashSet<Guid>();
                foreach (var change in window)
                {
                    if (!long.TryParse(change.Sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ||
                        sequence <= previous || !identities.Add(change.EventId))
                        throw new InvalidOperationException("Invalid notification journal order.");
                    previous = sequence;
                }
                planned = window.Take(50).ToArray(); more = window.Count > 50;
                admitted = await notifications.FindVisibleAsync(organizationId, recipientId,
                    planned.Select(e => e.EntityId).Distinct().ToArray(), policy.RequireVerifiedEmail, ct);
                return await LockAndVerify(organizationId, recipientId, admitted, ct);
            }, async () => {
                if (after < 0) return WorkOperation<SyncPage>.Failure("invalid_notification_cursor");
                var current = await notifications.FindVisibleAsync(organizationId, recipientId,
                    admitted.Select(n => n.Id).ToArray(), policy.RequireVerifiedEmail, ct);
                if (current.Count != admitted.Count || !current.Select(n => n.Id).ToHashSet().SetEquals(admitted.Select(n => n.Id)) ||
                    !await actors.VerifyAsync(recipientId, ct))
                    return WorkOperation<SyncPage>.Failure("notification_not_found");
                var visible = current.Select(n => n.Id).ToHashSet();
                return WorkOperation<SyncPage>.Success(new(organizationId, recipientId,
                    planned.Count == 0 ? after.ToString(System.Globalization.CultureInfo.InvariantCulture) : planned[^1].Sequence,
                    more, planned.Where(e => visible.Contains(e.EntityId)).ToArray()));
            }, ct);
    }

    public Task<WorkOperation<NotificationInboxPage>> ListAsync(Guid organizationId, Guid recipientId,
        NotificationCursor? after, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || recipientId == Guid.Empty)
            return Task.FromResult(WorkOperation<NotificationInboxPage>.Failure("notification_not_found"));
        IReadOnlyList<CardNotification> planned = [];
        return transactions.ExecuteAsync(organizationId,
            WorkCommand.Create(recipientId, null, "NotificationInbox", organizationId, new { }, "notification_not_found"),
            async _ =>
            {
                if (!await AdmitOrganization(organizationId, recipientId, ct)) return false;
                if (after?.Id == Guid.Empty) return true;
                planned = await notifications.ListVisibleAsync(organizationId, recipientId, after, policy.RequireVerifiedEmail, ct);
                return await LockAndVerify(organizationId, recipientId, planned, ct);
            }, async () =>
            {
                if (after?.Id == Guid.Empty) return WorkOperation<NotificationInboxPage>.Failure("invalid_notification_cursor");
                var current = await notifications.FindVisibleAsync(organizationId, recipientId,
                    planned.Select(n => n.Id).ToArray(), policy.RequireVerifiedEmail, ct);
                if (current.Count != planned.Count) return WorkOperation<NotificationInboxPage>.Failure("notification_not_found");
                if (!await actors.VerifyAsync(recipientId, ct)) return WorkOperation<NotificationInboxPage>.Failure("session_unavailable");
                var items = current.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id.ToString("N"), StringComparer.Ordinal)
                    .Take(50).ToArray();
                return WorkOperation<NotificationInboxPage>.Success(new(organizationId, items.Select(n => new NotificationInboxItem(
                    n.Id, n.RecipientId, n.ActorId, n.NotificationType, "Card", n.CardId, n.BoardId,
                    $"/app/{organizationId:D}/boards/{n.CurrentBoardId ?? n.BoardId:D}/cards/{n.CardId:D}", n.CreatedAt, n.ReadAt,
                    n.CurrentBoardId == n.BoardId ? null : n.CurrentBoardId)).ToArray(),
                    current.Count > 50 ? new NotificationCursor(items[^1].CreatedAt, items[^1].Id).ToString() : null));
            }, ct);
    }

    // Bulk applies to an explicit bounded set, so newly arrived notifications
    // cannot be accidentally marked read by a retried command.
    public Task<WorkOperation<NotificationReadResult>> MarkReadAsync(Guid organizationId, Guid recipientId,
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || recipientId == Guid.Empty)
            return Task.FromResult(WorkOperation<NotificationReadResult>.Failure("notification_not_found"));
        var valid = ids.Count is > 0 and <= 50 && !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Count;
        var ordered = ids.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        return transactions.ExecuteAsync(organizationId,
            WorkCommand.Create(recipientId, context.IdempotencyKey, "ReadNotifications", organizationId, new { ids = ordered }, "notification_not_found"),
            async _ =>
            {
                if (!await AdmitOrganization(organizationId, recipientId, ct)) return false;
                if (!valid) return true;
                var rows = await notifications.FindVisibleAsync(organizationId, recipientId, ordered, policy.RequireVerifiedEmail, ct);
                return rows.Count == ordered.Length && await LockAndVerify(organizationId, recipientId, rows, ct);
            }, async () =>
            {
                if (!valid) return WorkOperation<NotificationReadResult>.Failure("invalid_notification_selection");
                var rows = await notifications.FindVisibleAsync(organizationId, recipientId, ordered, policy.RequireVerifiedEmail, ct);
                if (rows.Count != ordered.Length) return WorkOperation<NotificationReadResult>.Failure("notification_not_found");
                var result = await notifications.MarkReadAsync(organizationId, recipientId, ordered, clock.UtcNow, ct);
                if (result.Count != ordered.Length) return WorkOperation<NotificationReadResult>.Failure("notification_not_found");
                if (!await actors.VerifyAsync(recipientId, ct)) return WorkOperation<NotificationReadResult>.Failure("session_unavailable");
                return WorkOperation<NotificationReadResult>.Success(new(organizationId, result));
            }, ct);
    }

    private async Task<bool> AdmitOrganization(Guid org, Guid recipient, CancellationToken ct) =>
        org != Guid.Empty && recipient != Guid.Empty && await work.AcquireOrganizationReadScopeAsync(org, recipient, ct) &&
        await organizations.FindOrganizationAsync(org, ct) is { Status: OrganizationStatus.Active or OrganizationStatus.Archived } &&
        await organizations.FindMembershipAsync(org, recipient, ct) is { Active: true };

    private async Task<bool> LockAndVerify(Guid org, Guid recipient, IReadOnlyList<CardNotification> rows, CancellationToken ct)
    {
        if (rows.Any(n => n.CurrentBoardId is null || n.CurrentBoardId == Guid.Empty)) return false;
        foreach (var boardId in rows.SelectMany(n => new[] { n.BoardId, n.CurrentBoardId ?? n.BoardId }).Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal))
        {
            if (!await work.AcquireBoardReadScopeAsync(org, recipient, boardId, ct)) return false;
            var view = await boards.GetSyncScopeAsync(boardId, recipient, ct);
            if (view.Value is not { Access.CanView: true, Board.LifecycleState: BoardLifecycleState.Active } ||
                view.Value.Board.OrganizationId != org) return false;
        }
        // One fresh joined eligibility read after all Board waits. It uses the
        // held parent locks shared with Card movement and lifecycle commands.
        if (rows.Any(n => n.OrganizationId != org || n.RecipientId != recipient)) return false;
        var ids = rows.Select(n => n.Id).ToArray();
        var current = await notifications.FindVisibleAsync(org, recipient, ids, policy.RequireVerifiedEmail, ct);
        return current.Count == rows.Count && current.Select(n => n.Id).ToHashSet().SetEquals(ids)
            && current.All(n => rows.Any(planned => planned.Id == n.Id && planned.BoardId == n.BoardId
                && planned.CardId == n.CardId && planned.CurrentBoardId == n.CurrentBoardId));
    }
}
