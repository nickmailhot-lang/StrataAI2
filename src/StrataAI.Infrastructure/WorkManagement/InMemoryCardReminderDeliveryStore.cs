using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

// Demo uses the same canonical generation, recipient and lease rules as the
// Production Worker capability. Both gates exclude account/parent changes.
internal sealed class InMemoryCardReminderDeliveryStore(InMemoryBackgroundJobStore jobs,
    InMemoryCardReminderStore reminders, InMemoryWorkManagementStore work,
    IWorkEventStore events, InMemoryWorkNotificationStore notifications,
    IOrganizationStore organizations, IIdentityStore identities, IdentityPolicy policy,
    IClock clock, InMemoryAccountOrganizationGate gate, DemoWorkTransactionScope scope,
    IEnumerable<IDemoWorkTransactionParticipant> participants) : ICardReminderDeliveryStore
{
    public async Task<CardReminderDeliveryResult> DeliverAsync(ClaimedBackgroundJob job,
        CardReminderAttempt attempt, CancellationToken ct)
    {
        if (job.JobType != CardReminderDeliveryHandler.Type || job.ServiceIdentity != CardReminderDeliveryHandler.Service ||
            job.Id == Guid.Empty || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty ||
            job.LeaseId == Guid.Empty || job.WorkerId == Guid.Empty || attempt != CardReminderAttempt.Parse(job.SafeMetadataJson))
            throw new InvalidOperationException("Invalid reminder delivery claim.");
        var key = $"card-reminder/{attempt.ReminderId:N}/{attempt.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        await gate.Commands.WaitAsync(ct);
        try
        {
            await gate.WorkCommands.WaitAsync(ct);
            try
            {
                using var owned = scope.Enter(job.OrganizationId);
                Action[] rollback = []; var committed = false;
                try
                {
                    if (!jobs.HasLiveClaim(job, key)) return CardReminderDeliveryResult.LeaseLost;
                    var row = reminders.FindById(job.OrganizationId, attempt.ReminderId);
                    if (row is null || row.Generation != attempt.Generation || !row.Enabled || row.Status is not ("SCHEDULED" or "FIRED"))
                        return CardReminderDeliveryResult.Superseded;
                    if (row.Status == "FIRED") return CardReminderDeliveryResult.Delivered;
                    var organization = await organizations.FindOrganizationAsync(row.OrganizationId, ct);
                    var member = await organizations.FindMembershipAsync(row.OrganizationId, row.UserId, ct);
                    var user = await identities.FindUserByIdAsync(row.UserId, ct);
                    var card = await work.FindCardAsync(row.CardId, ct);
                    var board = card is null ? null : await work.FindBoardAsync(card.BoardId, ct);
                    var list = card is null ? null : await work.FindListAsync(card.ListId, ct);
                    if (organization?.Status != OrganizationStatus.Active || member is not { Active: true } ||
                        user is not { Status: AccountStatus.Active } || policy.RequireVerifiedEmail && !user.EmailVerified ||
                        card is not { LifecycleState: WorkItemLifecycleState.Active, DueComplete: false } ||
                        card.OrganizationId != row.OrganizationId || card.DueAt != row.DueAt ||
                        board is not { LifecycleState: BoardLifecycleState.Active } || board.OrganizationId != row.OrganizationId ||
                        list is not { LifecycleState: WorkItemLifecycleState.Active } || list.OrganizationId != row.OrganizationId || list.BoardId != board.Id)
                        return CardReminderDeliveryResult.Superseded;
                    if (board.Visibility == BoardVisibility.Private && member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin) &&
                        await work.FindBoardMemberAsync(board.Id, row.UserId, ct) is not { Active: true })
                        return CardReminderDeliveryResult.Superseded;
                    if (row.TriggerAt is null || row.TriggerAt > clock.UtcNow) return CardReminderDeliveryResult.LeaseLost;
                    rollback = participants.Select(p => p.CaptureRollback()).ToArray();
                    var time = clock.UtcNow > row.UpdatedAt ? clock.UtcNow : row.UpdatedAt;
                    var fired = reminders.Fire(row, time);
                    var source = new WorkEvent(job.Id, row.OrganizationId, board.Id, job.ActorId,
                        "REMINDER_FIRED", "Reminder", row.Id, fired.Version, job.CorrelationId, time);
                    await events.AppendAsync(source, ct);
                    await notifications.AppendReminderAsync(new(job.Id, row.OrganizationId, board.Id, card.Id,
                        job.Id, row.UserId, job.ActorId, card.Version, time, null) { NotificationType = "REMINDER_FIRED" }, ct);
                    await work.AppendAuditFactAsync(job.Id, row.OrganizationId, job.ActorId, "REMINDER_FIRED", "Reminder",
                        row.Id, job.CorrelationId, time, ct);
                    ct.ThrowIfCancellationRequested();
                    if (!jobs.HasLiveClaim(job, key)) return CardReminderDeliveryResult.LeaseLost;
                    committed = true; return CardReminderDeliveryResult.Delivered;
                }
                finally { if (!committed) foreach (var restore in rollback.Reverse()) restore(); }
            }
            finally { gate.WorkCommands.Release(); }
        }
        finally { gate.Commands.Release(); }
    }
}
