using StrataAI.Application.Common;
using StrataAI.Application.Identity;

namespace StrataAI.Application.WorkManagement;

public sealed class CardDateService(IWorkManagementStore work, ICardDateStore dates, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, IWorkCommandContext context, ICommandActorAuthorization actors,
    IClock clock, IWorkEventStore events, CardWatchNotificationProducer notifications, CardReminderScheduling reminders)
{
    public async Task<WorkOperation<CardDateChange>> SetAsync(Guid cardId, Guid actor, CardDatesInput input,
        string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardDateChange>.Failure("card_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "CardDates", cardId, input, "card_not_found"),
            async receipt =>
            {
                if (receipt is not null && (receipt.Card.Id != cardId || receipt.Card.OrganizationId != hint.OrganizationId ||
                    receipt.Card.BoardId != hint.BoardId)) return false;
                if (!await work.AcquireCommandScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct)) return false;
                var view = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
                if (view.Value is not { Access.CanEdit: true, Board.LifecycleState: BoardLifecycleState.Active } ||
                    view.Value.Board.OrganizationId != hint.OrganizationId) return false;
                var current = await work.FindCardAsync(cardId, ct);
                if (current is not { LifecycleState: WorkItemLifecycleState.Active } || current.OrganizationId != hint.OrganizationId ||
                    current.BoardId != hint.BoardId || current.ListId != hint.ListId) return false;
                var list = await work.FindListAsync(current.ListId, ct);
                return list is { LifecycleState: WorkItemLifecycleState.Active } && list.OrganizationId == hint.OrganizationId && list.BoardId == hint.BoardId;
            }, async () =>
            {
                if (input.Version < 1) return WorkOperation<CardDateChange>.Failure("invalid_card_date_version");
                if (!CardDateInput.TryNormalize(input, out var normalized)) return WorkOperation<CardDateChange>.Failure("invalid_card_dates");
                var current = await work.FindCardAsync(cardId, ct);
                if (current is null || current.Version != input.Version) return WorkOperation<CardDateChange>.Failure("version_conflict");
                var old = CardDateValues.From(current);
                if (old == normalized) return WorkOperation<CardDateChange>.Success(new(current, false));
                var updated = await dates.SetDatesAsync(hint.OrganizationId, hint.BoardId, cardId, normalized!, input.Version, clock.UtcNow, ct);
                if (updated is null) return WorkOperation<CardDateChange>.Failure("version_conflict");
                await reminders.RescheduleAsync(current, updated, actor, correlationId, ct);
                async Task Publish(string type)
                {
                    await work.AppendAuditAsync(updated.OrganizationId, actor, type, "Card", cardId, correlationId, ct);
                    var change = new WorkEvent(Guid.NewGuid(), updated.OrganizationId, updated.BoardId, actor, type,
                        "Card", cardId, updated.Version, correlationId, clock.UtcNow);
                    await events.AppendAsync(change, ct); await notifications.AppendAsync(change, null, ct);
                }
                if (old.StartAt != normalized!.StartAt || old.DueAt != normalized.DueAt || old.DueTimezone != normalized.DueTimezone ||
                    old.DueHasTime != normalized.DueHasTime) await Publish("CARD_DATE_CHANGED");
                if (old.DueComplete != normalized.DueComplete && normalized.DueAt is not null)
                    await Publish(normalized.DueComplete ? "CARD_DUE_COMPLETED" : "CARD_DUE_REOPENED");
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<CardDateChange>.Failure("session_unavailable");
                return WorkOperation<CardDateChange>.Success(new(updated, true));
            }, ct);
    }
}
