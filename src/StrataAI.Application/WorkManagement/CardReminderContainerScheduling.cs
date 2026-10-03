using StrataAI.Application.Common;

namespace StrataAI.Application.WorkManagement;

// The container command owns authorization, its Board gate and transaction.
// A hidden parent changes eligibility without changing a child's archive state.
public sealed class CardReminderContainerScheduling(IWorkManagementStore work, ICardReminderStore reminders,
    ICardReminderJobPublisher jobs, ICardReminderEventPublisher events, IClock clock)
{
    public async Task RescheduleAsync(Guid organizationId, Guid boardId, Guid? listId, Guid actorId, string correlationId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty || boardId == Guid.Empty || actorId == Guid.Empty || listId == Guid.Empty)
            throw new InvalidOperationException("Invalid Reminder container scope.");
        var board = await work.FindBoardAsync(boardId, ct);
        var now = clock.UtcNow;
        foreach (var id in await work.ListReminderCandidateCardIdsAsync(organizationId, boardId, listId, ct))
        {
            var card = await work.FindCardAsync(id, ct, includeDeleted: true);
            if (card is null || card.OrganizationId != organizationId || card.BoardId != boardId || listId is not null && card.ListId != listId)
                throw new InvalidOperationException("Invalid Reminder container candidate.");
            var parent = await work.FindListAsync(card.ListId, ct, includeDeleted: true);
            var active = board is { LifecycleState: BoardLifecycleState.Active } && board.OrganizationId == organizationId &&
                parent is { LifecycleState: WorkItemLifecycleState.Active } && parent.OrganizationId == organizationId && parent.BoardId == boardId;
            foreach (var candidate in await reminders.ListEnabledForCardAsync(organizationId, card.Id, ct))
            {
                if (candidate.OrganizationId != organizationId || candidate.CardId != card.Id || !candidate.Enabled)
                    throw new InvalidOperationException("Invalid Reminder container recipient.");
                var updated = await reminders.SetAsync(card, candidate.UserId, candidate.IntervalCode, true, candidate.Version, now, ct, contextActive: active);
                if (updated is null) throw new InvalidOperationException("Reminder changed during its owning container transaction.");
                if (updated.Generation == candidate.Generation) continue;
                if (updated.Status == "SCHEDULED") await jobs.PublishAsync(updated, actorId, correlationId, ct);
                await events.PublishAsync(card, updated, actorId, correlationId, ct);
            }
        }
    }
}
