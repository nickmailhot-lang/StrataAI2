using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresAttachmentPreviewIntentStore(PostgresConnectionFactory connections) : IAttachmentPreviewIntentStore
{
    private static void Validate(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt)
    {
        if (job.JobType != AttachmentPreviewJobs.Type || job.ServiceIdentity != AttachmentPreviewJobs.Service
            || job.Id == Guid.Empty || job.OrganizationId == Guid.Empty || job.ActorId == Guid.Empty
            || job.WorkerId == Guid.Empty || job.LeaseId == Guid.Empty || job.AttemptCount < 1
            || attempt != AttachmentPreviewAttempt.Parse(job.SafeMetadataJson)) throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Attachment preview intent is unavailable.");
    private static void AddClaim(NpgsqlCommand query, ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt)
    {
        query.Parameters.AddWithValue("job", job.Id); query.Parameters.AddWithValue("tenant", job.OrganizationId);
        query.Parameters.AddWithValue("actor", job.ActorId); query.Parameters.AddWithValue("worker", job.WorkerId);
        query.Parameters.AddWithValue("lease", job.LeaseId); query.Parameters.AddWithValue("attachment", attempt.AttachmentId);
        query.Parameters.AddWithValue("card", attempt.CardId); query.Parameters.AddWithValue("version", attempt.Version);
    }
    public async Task<AttachmentPreviewLoad> LoadAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt, CancellationToken ct)
    {
        Validate(job, attempt); ct.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, ct);
        await using var query = new NpgsqlCommand("SELECT * FROM public.load_attachment_preview(@job,@tenant,@actor,@worker,@lease,@attachment,@card,@version);", session.Connection, session.Transaction);
        AddClaim(query, job, attempt); AttachmentPreviewLoad loaded;
        await using (var row = await query.ExecuteReaderAsync(ct))
        {
            if (!await row.ReadAsync(ct)) throw Unavailable();
            var status = row.GetString(0) switch
            {
                "READY" => AttachmentPreviewLoadStatus.Ready, "SUPERSEDED" => AttachmentPreviewLoadStatus.Superseded,
                "LEASE_LOST" => AttachmentPreviewLoadStatus.LeaseLost, _ => throw Unavailable()
            };
            if (status == AttachmentPreviewLoadStatus.Ready)
            {
                if (row.IsDBNull(1) || row.IsDBNull(2) || row.IsDBNull(3)
                    || row.GetString(3) is not ("image/png" or "image/jpeg" or "image/webp")) throw Unavailable();
                AttachmentPreviewMeasurement? output = null;
                var hasOutput = !row.IsDBNull(4);
                for (var index = 5; index < 8; index++) if (row.IsDBNull(index) == hasOutput) throw Unavailable();
                if (hasOutput) output = new(row.GetInt64(4), row.GetString(5), row.GetInt32(6), row.GetInt32(7));
                loaded = new(status, new(new(job.OrganizationId, attempt.AttachmentId), row.GetInt64(1), row.GetString(2)), row.GetString(3), output);
            }
            else
            {
                for (var index = 1; index < 8; index++) if (!row.IsDBNull(index)) throw Unavailable();
                loaded = new(status);
            }
            if (await row.ReadAsync(ct)) throw Unavailable();
        }
        if (loaded.Status != AttachmentPreviewLoadStatus.LeaseLost) await session.CommitAsync(ct);
        return loaded;
    }
    public async Task<AttachmentPreviewDeclaration> DeclareAsync(ClaimedBackgroundJob job, AttachmentPreviewAttempt attempt,
        AttachmentScanRequest source, string verifiedMimeType, AttachmentPreviewMeasurement output, CancellationToken ct)
    {
        Validate(job, attempt); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(output);
        if (source.Reference.OrganizationId != job.OrganizationId || source.Reference.AttachmentId != attempt.AttachmentId
            || verifiedMimeType is not ("image/png" or "image/jpeg" or "image/webp")) throw Unavailable();
        ct.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(job.OrganizationId, ct);
        await using var query = new NpgsqlCommand("""
            SELECT public.declare_attachment_preview(@job,@tenant,@actor,@worker,@lease,@attachment,@card,@version,
                @source_size,@source_digest,@mime,@output_size,@output_digest,@width,@height);
            """, session.Connection, session.Transaction);
        AddClaim(query, job, attempt); query.Parameters.AddWithValue("source_size", source.SizeBytes);
        query.Parameters.AddWithValue("source_digest", source.Sha256); query.Parameters.AddWithValue("mime", verifiedMimeType);
        query.Parameters.AddWithValue("output_size", output.SizeBytes); query.Parameters.AddWithValue("output_digest", output.Sha256);
        query.Parameters.AddWithValue("width", output.Width); query.Parameters.AddWithValue("height", output.Height);
        var result = await query.ExecuteScalarAsync(ct) switch
        {
            "DECLARED" => AttachmentPreviewDeclaration.Declared, "CONFLICT" => AttachmentPreviewDeclaration.Conflict,
            "SUPERSEDED" => AttachmentPreviewDeclaration.Superseded, "LEASE_LOST" => AttachmentPreviewDeclaration.LeaseLost,
            _ => throw Unavailable()
        };
        if (result == AttachmentPreviewDeclaration.Declared) await session.CommitAsync(ct);
        return result;
    }
}