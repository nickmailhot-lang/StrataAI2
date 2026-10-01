using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

public sealed class PostgresInvitationDeliveryStore(PostgresConnectionFactory connections, bool requireVerifiedEmail)
    : IInvitationDeliveryStore
{
    public async Task<InvitationMailIntent?> LoadAsync(ClaimedBackgroundJob job, CancellationToken cancellationToken)
    {
        Validate(job);
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        InvitationMailIntent? result;
        await using (var command = new NpgsqlCommand("SELECT * FROM public.load_invitation_mail(@job,@tenant,@actor,@worker,@lease,@verified);", session.Connection, session.Transaction))
        {
            Bind(command, job);
            command.Parameters.AddWithValue("verified", requireVerifiedEmail);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            result = await reader.ReadAsync(cancellationToken) ? new(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4),
                reader.GetString(5) switch { "INTERNAL" => InvitationSurface.Internal, "PORTAL" => InvitationSurface.Portal,
                    _ => throw new InvalidOperationException("Invalid invitation surface.") },
                reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetString(8), reader.GetString(9),
                reader.GetString(10), reader.GetString(11), reader.GetInt32(12),
                reader.GetString(13) switch { "PENDING" => InvitationMailState.Pending, "SENT" => InvitationMailState.Sent,
                    "CANCELLED" => InvitationMailState.Cancelled, "FAILED" => InvitationMailState.Failed,
                    _ => throw new InvalidOperationException("Invalid invitation mail state.") },
                reader.IsDBNull(14) ? null : reader.GetString(14), reader.GetBoolean(15)) : null;
        }
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> FinishAsync(ClaimedBackgroundJob job, InvitationMailState state, string? safeErrorCode,
        Guid? providerReceiptId, CancellationToken cancellationToken)
    {
        Validate(job);
        var databaseState = state switch { InvitationMailState.Sent => "SENT", InvitationMailState.Cancelled => "CANCELLED",
            InvitationMailState.Failed => "FAILED", _ => throw new ArgumentOutOfRangeException(nameof(state)) };
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT public.finish_invitation_mail(@job,@tenant,@actor,@worker,@lease,@state,@error,@receipt);", session.Connection, session.Transaction);
        Bind(command, job);
        command.Parameters.AddWithValue("state", databaseState);
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, (object?)safeErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("receipt", NpgsqlDbType.Uuid, (object?)providerReceiptId ?? DBNull.Value);
        var changed = await command.ExecuteScalarAsync(cancellationToken) is true;
        await session.CommitAsync(cancellationToken);
        return changed;
    }

    private static void Bind(NpgsqlCommand command, ClaimedBackgroundJob job)
    {
        command.Parameters.AddWithValue("job", job.Id);
        command.Parameters.AddWithValue("tenant", job.OrganizationId);
        command.Parameters.AddWithValue("actor", job.ActorId);
        command.Parameters.AddWithValue("worker", job.WorkerId);
        command.Parameters.AddWithValue("lease", job.LeaseId);
    }

    private static void Validate(ClaimedBackgroundJob job)
    {
        if (job.JobType != InvitationEmailHandler.Type || job.ServiceIdentity != InvitationEmailHandler.Identity
            || job.Id == Guid.Empty || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty
            || job.WorkerId == Guid.Empty || job.LeaseId == Guid.Empty)
            throw new InvalidOperationException("Invalid invitation delivery claim.");
    }
}
