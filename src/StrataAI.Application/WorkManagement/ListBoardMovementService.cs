using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

// The command owns authorization, receipt, final admission and rollback. Every
// publication below borrows that transaction; this is not a second commit.
public sealed class ListBoardMovementService(ListBoardMovementCommand command, IListBoardMovementStore movement,
    IWorkManagementStore work, IWorkEventStore events, CardWatchNotificationProducer watches,
    CardReminderScheduling reminders, IdentityPolicy policy, IClock clock)
{
    public Task<WorkOperation<BoardListRecord>> MoveAsync(Guid listId, Guid actorId, Guid sourceBoardId,
        Guid destinationBoardId, long expectedVersion, Guid? beforeListId, string correlationId, CancellationToken ct)
        => command.ExecuteAsync(listId, actorId, sourceBoardId, destinationBoardId, expectedVersion, beforeListId, async () =>
        {
            var original = await work.FindListAsync(listId, ct);
            if (original is null || original.BoardId != sourceBoardId)
                return WorkOperation<BoardListRecord>.Failure("list_not_found");
            ListBoardTransfer? transfer;
            try
            {
                transfer = await movement.TransferListBoardAsync(original.OrganizationId, sourceBoardId, listId, destinationBoardId,
                    expectedVersion, clock.UtcNow, policy.RequireVerifiedEmail, beforeListId, ct);
            }
            catch (RankSpaceExhaustedException)
            {
                return WorkOperation<BoardListRecord>.Failure("rank_space_exhausted");
            }
            if (transfer is null) return WorkOperation<BoardListRecord>.Failure("version_conflict");
            if (transfer.List.Id != listId || transfer.List.OrganizationId != original.OrganizationId
                || transfer.List.BoardId != destinationBoardId || transfer.List.Version != expectedVersion + 1)
                throw new InvalidOperationException("List transfer publication scope does not match its command.");
            var candidates = (await work.ListReminderCandidateCardIdsAsync(original.OrganizationId, destinationBoardId, listId, ct)).ToHashSet();
            var seen = new HashSet<Guid>();
            foreach (var subject in transfer.Cards)
            {
                ct.ThrowIfCancellationRequested();
                var current = await work.FindCardAsync(subject.Id, ct, includeDeleted: true);
                if (!seen.Add(subject.Id) || current is null || current.OrganizationId != original.OrganizationId
                    || current.BoardId != destinationBoardId || current.ListId != listId
                    || current.Version != checked(subject.SourceVersion + 1) || current.LifecycleState != subject.LifecycleState)
                    throw new InvalidOperationException("List transfer Card publication does not match persisted state.");
                await work.AppendAuditAsync(current.OrganizationId, actorId, "CARD_MOVED", "Card", current.Id, correlationId, ct);
                var change = new WorkEvent(Guid.NewGuid(), current.OrganizationId, destinationBoardId, actorId,
                    "CARD_MOVED", "Card", current.Id, current.Version, correlationId, clock.UtcNow);
                await events.AppendAsync(change, ct);
                await events.AppendAsync(change with { EventId = Guid.NewGuid(), BoardId = sourceBoardId }, ct);
                await watches.AppendAsync(change, null, ct);
                if (candidates.Contains(current.Id))
                    await reminders.RescheduleAsync(current with { BoardId = sourceBoardId, Version = subject.SourceVersion },
                        current, actorId, correlationId, ct);
            }
            if (!candidates.IsSubsetOf(seen))
                throw new InvalidOperationException("List transfer omitted a Reminder candidate.");
            await work.AppendAuditAsync(original.OrganizationId, actorId, "LIST_MOVED", "List", listId, correlationId, ct);
            var listChange = new WorkEvent(Guid.NewGuid(), original.OrganizationId, destinationBoardId, actorId,
                "LIST_MOVED", "List", listId, transfer.List.Version, correlationId, clock.UtcNow);
            await events.AppendAsync(listChange, ct);
            await events.AppendAsync(listChange with { EventId = Guid.NewGuid(), BoardId = sourceBoardId }, ct);
            return WorkOperation<BoardListRecord>.Success(transfer.List);
        }, ct);
}
