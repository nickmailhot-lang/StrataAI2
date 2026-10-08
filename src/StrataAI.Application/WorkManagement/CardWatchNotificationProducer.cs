using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

// Runs under the originating mutation's Board gate and owning transaction. Do
// not depend on IWorkBoardAuthorization: its implementation owns this producer.
public sealed class CardWatchNotificationProducer(IWorkManagementStore work, ICardWatchRecipientStore eligible,
    IOrganizationStore organizations, IdentityPolicy policy, IWorkNotificationStore notifications)
{
    public async Task AppendAsync(WorkEvent change, Guid? assignmentRecipient, CancellationToken ct)
    {
        if (!CardWatchActivity.IsRelevant(change)) return;
        var card = await work.FindCardAsync(change.EntityId, ct);
        if (card is null || card.LifecycleState != (change.EventType == "CARD_ARCHIVED"
            ? WorkItemLifecycleState.Archived : WorkItemLifecycleState.Active)) return;
        var scope = CardWatchActivity.Capture(change, card)!;
        var board = await work.FindBoardAsync(scope.BoardId, ct);
        var list = await work.FindListAsync(scope.ListId, ct);
        if (board is not { LifecycleState: BoardLifecycleState.Active } || board.OrganizationId != scope.OrganizationId ||
            board.Visibility is not (BoardVisibility.Private or BoardVisibility.Organization or BoardVisibility.Public) ||
            list is not { LifecycleState: WorkItemLifecycleState.Active } || list.OrganizationId != scope.OrganizationId ||
            list.BoardId != scope.BoardId || await organizations.FindOrganizationAsync(scope.OrganizationId, ct)
                is not { Status: OrganizationStatus.Active }) return;
        var recipients = new List<Guid>();
        foreach (var recipient in await eligible.LockRecipientsAsync(scope, policy.RequireVerifiedEmail, ct))
        {
            // Assignment wins overlapping watch intent for the same event.
            if (recipient != change.ActorId && recipient != assignmentRecipient) recipients.Add(recipient);
        }
        await notifications.AppendCardActivitiesAsync(change, recipients, ct);
    }
}
