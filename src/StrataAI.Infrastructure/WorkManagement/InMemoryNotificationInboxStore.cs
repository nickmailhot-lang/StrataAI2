using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryNotificationInboxStore(InMemoryWorkNotificationStore notifications,
    IWorkManagementStore work, IOrganizationStore organizations, IIdentityStore identities,
    IWorkBoardAuthorization boards) : INotificationInboxStore
{
    public async Task<IReadOnlyList<CardAssignmentNotification>> ListVisibleAsync(Guid org, Guid recipient,
        NotificationCursor? after, bool verified, CancellationToken ct)
    {
        var rows = await Visible(org, recipient, null, verified, ct);
        return rows.Where(n => after is null || n.CreatedAt < after.CreatedAt || n.CreatedAt == after.CreatedAt &&
            string.CompareOrdinal(n.Id.ToString("N"), after.Id.ToString("N")) < 0)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id.ToString("N"), StringComparer.Ordinal).Take(51).ToArray();
    }

    public Task<IReadOnlyList<CardAssignmentNotification>> FindVisibleAsync(Guid org, Guid recipient,
        IReadOnlyCollection<Guid> ids, bool verified, CancellationToken ct) => Visible(org, recipient, ids, verified, ct);

    private async Task<IReadOnlyList<CardAssignmentNotification>> Visible(Guid org, Guid recipient, IReadOnlyCollection<Guid>? ids, bool verified, CancellationToken ct)
    {
        if (ids is { Count: > 51 }) throw new ArgumentException("Notification windows contain at most 51 records.", nameof(ids));
        var account = await identities.FindUserByIdAsync(recipient, ct);
        if (account is not { Status: AccountStatus.Active } || verified && !account.EmailVerified ||
            await organizations.FindMembershipAsync(org, recipient, ct) is not { Active: true } ||
            await organizations.FindOrganizationAsync(org, ct) is not { Status: OrganizationStatus.Active or OrganizationStatus.Archived }) return [];
        var result = new List<CardAssignmentNotification>();
        foreach (var item in notifications.Snapshot(org, recipient).Where(n => ids is null || ids.Contains(n.Id)))
        {
            var view = await boards.GetSyncScopeAsync(item.BoardId, recipient, ct);
            var card = await work.FindCardAsync(item.CardId, ct);
            if (view.Value is not { Access.CanView: true, Board.LifecycleState: BoardLifecycleState.Active } || view.Value.Board.OrganizationId != org ||
                card is not { LifecycleState: WorkItemLifecycleState.Active } || card.OrganizationId != org || card.BoardId != item.BoardId) continue;
            var list = await work.FindListAsync(card.ListId, ct);
            if (list is { LifecycleState: WorkItemLifecycleState.Active } && list.OrganizationId == org && list.BoardId == item.BoardId) result.Add(item);
        }
        return result;
    }

    public Task<IReadOnlyList<NotificationReadAcknowledgment>> MarkReadAsync(Guid org, Guid recipient,
        IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct) => Task.FromResult(notifications.MarkRead(org, recipient, ids, now));
}
