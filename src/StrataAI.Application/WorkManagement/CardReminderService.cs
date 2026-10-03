using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record CardReminderInput(string? IntervalCode, bool Enabled, long CardVersion, long Version);
public sealed record CardReminderState(Guid OrganizationId, Guid BoardId, Guid CardId, Guid UserId,
    long CardVersion, CardReminder? Reminder, IReadOnlyList<CardReminderOption> Options, bool CanChange, bool Changed);

// Personal choice: viewing the Card admits only this actor's Reminder. Shared
// Board/Card editing permission is neither granted nor required by this command.
public sealed class CardReminderService(ICardReminderStore reminders, ICardReminderJobPublisher jobs,
    IWorkManagementStore work, IOrganizationStore organizations, IWorkBoardAuthorization boards,
    IWorkManagementUnitOfWork transactions, IWorkCommandContext context, ICommandActorAuthorization actors,
    IClock clock, ICardReminderEventPublisher events)
{
    public Task<WorkOperation<CardReminderState>> GetAsync(Guid cardId, Guid actor, CancellationToken ct = default) =>
        Execute(cardId, actor, null, "", ct);
    public Task<WorkOperation<CardReminderState>> SetAsync(Guid cardId, Guid actor, CardReminderInput input,
        string correlationId, CancellationToken ct = default) => Execute(cardId, actor, input, correlationId, ct);

    private async Task<WorkOperation<CardReminderState>> Execute(Guid cardId, Guid actor, CardReminderInput? input,
        string correlationId, CancellationToken ct)
    {
        if (cardId == Guid.Empty || actor == Guid.Empty) return WorkOperation<CardReminderState>.Failure("card_reminder_not_found");
        var hint = await work.FindCardAsync(cardId, ct);
        if (hint is null) return WorkOperation<CardReminderState>.Failure("card_reminder_not_found");
        CardRecord? admitted = null; var canChange = false;
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, input is null ? null : context.IdempotencyKey, "CardReminder", cardId,
                new { input }, "card_reminder_not_found"), async receipt =>
            {
                if (receipt is not null && (receipt.OrganizationId != hint.OrganizationId || receipt.BoardId != hint.BoardId ||
                    receipt.CardId != cardId || receipt.UserId != actor || (receipt.Reminder is { } row &&
                    (row.OrganizationId != hint.OrganizationId || row.CardId != cardId || row.UserId != actor)))) return false;
                var locked = input is null
                    ? await work.AcquireBoardReadScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct)
                    : await work.AcquireCommandScopeAsync(hint.OrganizationId, actor, hint.BoardId, ct);
                if (!locked || await organizations.FindMembershipAsync(hint.OrganizationId, actor, ct) is not { Active: true }) return false;
                var organization = await organizations.FindOrganizationAsync(hint.OrganizationId, ct);
                if (organization is null || (input is not null ? organization.Status != OrganizationStatus.Active :
                    organization.Status is not (OrganizationStatus.Active or OrganizationStatus.Archived))) return false;
                canChange = organization.Status == OrganizationStatus.Active;
                var current = await work.FindCardAsync(cardId, ct);
                if (current is not { LifecycleState: WorkItemLifecycleState.Active } || current.OrganizationId != hint.OrganizationId ||
                    current.BoardId != hint.BoardId || current.ListId != hint.ListId) return false;
                var list = await work.FindListAsync(current.ListId, ct);
                if (list is not { LifecycleState: WorkItemLifecycleState.Active } || list.OrganizationId != hint.OrganizationId || list.BoardId != hint.BoardId) return false;
                var view = await boards.GetSyncScopeAsync(hint.BoardId, actor, ct);
                if (view.Value is not { Access.CanView: true, Board.LifecycleState: BoardLifecycleState.Active } ||
                    view.Value.Board.OrganizationId != hint.OrganizationId) return false;
                admitted = current; return true;
            }, async () =>
            {
                var card = admitted ?? throw new InvalidOperationException("Reminder admission was unavailable.");
                var current = await reminders.FindAsync(card.OrganizationId, actor, cardId, ct); var changed = false;
                if (current is not null && (current.OrganizationId != card.OrganizationId || current.CardId != cardId || current.UserId != actor))
                    return WorkOperation<CardReminderState>.Failure("card_reminder_not_found");
                var now = clock.UtcNow;
                if (input is not null)
                {
                    if (input.CardVersion < 1 || input.Version < 0) return WorkOperation<CardReminderState>.Failure("invalid_card_reminder_version");
                    if (input.CardVersion != card.Version || input.Version != (current?.Version ?? 0))
                        return WorkOperation<CardReminderState>.Failure("version_conflict");
                    if (input.Enabled && (input.IntervalCode is null || CardReminderIntervals.Find(card, input.IntervalCode, now) is null))
                        return WorkOperation<CardReminderState>.Failure("invalid_card_reminder_interval");
                    // Cancelling an absent choice is a no-op; never create a tombstone
                    // with an invented interval or disclose another user's choice.
                    if (input.Enabled || current is not null)
                    {
                        var updated = await reminders.SetAsync(card, actor, input.Enabled ? input.IntervalCode! : current!.IntervalCode,
                            input.Enabled, input.Version, now, ct);
                        if (updated is null) return WorkOperation<CardReminderState>.Failure("version_conflict");
                        changed = updated.Version != (current?.Version ?? 0); current = updated;
                        if (changed)
                        {
                            if (updated.Status == "SCHEDULED") await jobs.PublishAsync(updated, actor, correlationId, ct);
                            await events.PublishAsync(card, updated, actor, correlationId, ct);
                        }
                    }
                }
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<CardReminderState>.Failure("session_unavailable");
                return WorkOperation<CardReminderState>.Success(new(card.OrganizationId, card.BoardId, cardId, actor, card.Version,
                    current, canChange ? CardReminderIntervals.Available(card, clock.UtcNow) : [], canChange, changed));
            }, ct);
    }
}
