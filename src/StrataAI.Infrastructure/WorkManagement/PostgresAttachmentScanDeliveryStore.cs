using Npgsql;
using StrataAI.Application.BackgroundJobs;
using StrataAI.Application.WorkManagement;
using StrataAI.Domain.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresAttachmentScanDeliveryStore(PostgresConnectionFactory connections, bool previewEnabled = false) : IAttachmentScanDeliveryStore
{
    private static void Validate(ClaimedBackgroundJob job,AttachmentScanAttempt attempt)
    {
        if (job.JobType!=AttachmentScanJobs.Type || job.ServiceIdentity!=AttachmentScanJobs.Service || job.Id==Guid.Empty
            || job.OrganizationId==Guid.Empty || job.ActorId==Guid.Empty || job.WorkerId==Guid.Empty || job.LeaseId==Guid.Empty
            || job.AttemptCount<1 || attempt!=AttachmentScanAttempt.Parse(job.SafeMetadataJson))
            throw new InvalidOperationException("Attachment scan claim is unavailable.");
    }
    private static void AddClaim(NpgsqlCommand query,ClaimedBackgroundJob job,AttachmentScanAttempt attempt)
    {
        query.Parameters.AddWithValue("job",job.Id); query.Parameters.AddWithValue("tenant",job.OrganizationId);
        query.Parameters.AddWithValue("actor",job.ActorId); query.Parameters.AddWithValue("worker",job.WorkerId);
        query.Parameters.AddWithValue("lease",job.LeaseId); query.Parameters.AddWithValue("attachment",attempt.AttachmentId);
        query.Parameters.AddWithValue("card",attempt.CardId); query.Parameters.AddWithValue("version",attempt.Version);
    }
    public async Task<AttachmentScanLoad> LoadAsync(ClaimedBackgroundJob job,AttachmentScanAttempt attempt,CancellationToken ct)
    {
        Validate(job,attempt); ct.ThrowIfCancellationRequested();
        await using var session=await connections.OpenTenantSessionAsync(job.OrganizationId,ct);
        await using var query=new NpgsqlCommand("SELECT disposition,scan_size_bytes,scan_sha256 FROM public.load_attachment_scan(@job,@tenant,@actor,@worker,@lease,@attachment,@card,@version);",session.Connection,session.Transaction);
        AddClaim(query,job,attempt); AttachmentScanLoad loaded;
        await using(var row=await query.ExecuteReaderAsync(ct))
        {
            if(!await row.ReadAsync(ct)) throw new InvalidOperationException("Attachment scan load is unavailable.");
            var status=row.GetString(0) switch
            {
                "READY" => AttachmentScanLoadStatus.Ready,"APPLIED" => AttachmentScanLoadStatus.Applied,
                "SUPERSEDED" => AttachmentScanLoadStatus.Superseded,"LEASE_LOST" => AttachmentScanLoadStatus.LeaseLost,
                _ => throw new InvalidOperationException("Attachment scan load is unavailable.")
            };
            if(status==AttachmentScanLoadStatus.Ready)
            {
                if(row.IsDBNull(1)||row.IsDBNull(2)) throw new InvalidOperationException("Attachment scan integrity is unavailable.");
                loaded=new(status,new(new(job.OrganizationId,attempt.AttachmentId),row.GetInt64(1),row.GetString(2)));
            }
            else
            {
                if(!row.IsDBNull(1)||!row.IsDBNull(2)) throw new InvalidOperationException("Attachment scan integrity is unavailable.");
                loaded=new(status);
            }
            if(await row.ReadAsync(ct)) throw new InvalidOperationException("Attachment scan load is unavailable.");
        }
        if(loaded.Status!=AttachmentScanLoadStatus.LeaseLost) await session.CommitAsync(ct);
        return loaded;
    }
    public async Task<AttachmentScanCompletion> FinishAsync(ClaimedBackgroundJob job,AttachmentScanAttempt attempt,AttachmentScanEvidence evidence,CancellationToken ct)
    {
        Validate(job,attempt); ArgumentNullException.ThrowIfNull(evidence); ct.ThrowIfCancellationRequested();
        if(evidence.Request.Reference.OrganizationId!=job.OrganizationId || evidence.Request.Reference.AttachmentId!=attempt.AttachmentId)
            throw new InvalidOperationException("Attachment scan evidence is unavailable.");
        var status=evidence.Status switch
        {
            AttachmentScanStatus.Clean => "CLEAN", AttachmentScanStatus.Rejected => "REJECTED", AttachmentScanStatus.Failed => "FAILED",
            _ => throw new InvalidOperationException("Attachment scan evidence is unavailable.")
        };
        await using var session=await connections.OpenTenantSessionAsync(job.OrganizationId,ct);
        await using var query=new NpgsqlCommand("SELECT public.finish_attachment_scan(@job,@tenant,@actor,@worker,@lease,@attachment,@card,@version,@size,@digest,@status,@preview);",session.Connection,session.Transaction);
        AddClaim(query,job,attempt); query.Parameters.AddWithValue("size",evidence.Request.SizeBytes);
        query.Parameters.AddWithValue("digest",evidence.Request.Sha256); query.Parameters.AddWithValue("status",status);
        query.Parameters.AddWithValue("preview",previewEnabled);
        var result=await query.ExecuteScalarAsync(ct) switch
        {
            "APPLIED" => AttachmentScanCompletion.Applied,"SUPERSEDED" => AttachmentScanCompletion.Superseded,
            "RETRY" => AttachmentScanCompletion.Retry,"LEASE_LOST" => AttachmentScanCompletion.LeaseLost,
            _ => throw new InvalidOperationException("Attachment scan completion is unavailable.")
        };
        if(result is AttachmentScanCompletion.Applied or AttachmentScanCompletion.Superseded) await session.CommitAsync(ct);
        return result;
    }
}
