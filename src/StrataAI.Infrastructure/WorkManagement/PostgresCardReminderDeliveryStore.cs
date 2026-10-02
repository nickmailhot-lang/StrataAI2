using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresCardReminderDeliveryStore(PostgresConnectionFactory connections, bool requireVerifiedEmail)
    : ICardReminderDeliveryStore
{
    public async Task<CardReminderDeliveryResult> DeliverAsync(ClaimedBackgroundJob job, CardReminderAttempt attempt, CancellationToken ct)
    {
        if (job.JobType != CardReminderDeliveryHandler.Type || job.ServiceIdentity != CardReminderDeliveryHandler.Service ||
            job.Id == Guid.Empty || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty || job.LeaseId == Guid.Empty ||
            job.WorkerId == Guid.Empty || attempt != CardReminderAttempt.Parse(job.SafeMetadataJson))
            throw new InvalidOperationException("Invalid reminder delivery claim.");
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, ct);
        await using var query = new NpgsqlCommand("SELECT public.deliver_card_reminder(@job,@tenant,@actor,@worker,@lease,@reminder,@generation,@verified);",
            session.Connection, session.Transaction);
        query.Parameters.AddWithValue("job", job.Id); query.Parameters.AddWithValue("tenant", job.OrganizationId);
        query.Parameters.AddWithValue("actor", job.ActorId); query.Parameters.AddWithValue("worker", job.WorkerId);
        query.Parameters.AddWithValue("lease", job.LeaseId); query.Parameters.AddWithValue("reminder", attempt.ReminderId);
        query.Parameters.AddWithValue("generation", attempt.Generation); query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        var result = (await query.ExecuteScalarAsync(ct)) switch
        {
            "DELIVERED" => CardReminderDeliveryResult.Delivered, "SUPERSEDED" => CardReminderDeliveryResult.Superseded,
            "LEASE_LOST" => CardReminderDeliveryResult.LeaseLost, _ => throw new InvalidOperationException("Invalid reminder delivery result.")
        };
        if (result != CardReminderDeliveryResult.LeaseLost) await session.CommitAsync(ct);
        return result;
    }
}
