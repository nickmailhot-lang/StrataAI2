using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.BackgroundJobs;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCardReminderJobPublisher(PostgresConnectionFactory connections,
    PostgresBackgroundJobStore jobs) : ICardReminderJobPublisher
{
    public async Task PublishAsync(CardReminder reminder, Guid actorId, string correlationId, CancellationToken ct)
    {
        if (!connections.HasCommandScope(reminder.OrganizationId))
            throw new InvalidOperationException("Reminder jobs require the owning Card transaction.");
        var job = CardReminderJobs.Create(reminder, actorId, correlationId);
        await using var session = await connections.OpenTenantSessionAsync(reminder.OrganizationId, ct);
        await using (var canonical = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM card_reminders WHERE tenant_id=@tenant AND id=@id AND user_id=@user
              AND card_id=@card AND generation=@generation AND version=@version AND enabled AND status='SCHEDULED'
              AND interval_code=@interval AND due_at=@due AND trigger_at=@trigger);
            """, session.Connection, session.Transaction))
        {
            canonical.Parameters.AddWithValue("tenant", reminder.OrganizationId); canonical.Parameters.AddWithValue("id", reminder.Id);
            canonical.Parameters.AddWithValue("user", reminder.UserId); canonical.Parameters.AddWithValue("card", reminder.CardId);
            canonical.Parameters.AddWithValue("generation", reminder.Generation); canonical.Parameters.AddWithValue("version", reminder.Version);
            canonical.Parameters.AddWithValue("interval", reminder.IntervalCode); canonical.Parameters.AddWithValue("due", reminder.DueAt!.Value.ToUniversalTime());
            canonical.Parameters.AddWithValue("trigger", reminder.TriggerAt!.Value.ToUniversalTime());
            if (await canonical.ExecuteScalarAsync(ct) is not true)
                throw new InvalidOperationException("Reminder publication does not match its persisted generation.");
        }
        // A duplicate generation retains the first committed job/availability.
        await jobs.PublishAsync(session, job, ct);
        await session.CommitAsync(ct);
    }
}
