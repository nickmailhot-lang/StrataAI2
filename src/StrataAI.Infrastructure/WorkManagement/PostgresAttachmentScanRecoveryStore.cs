using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

public sealed class PostgresAttachmentScanRecoveryStore(PostgresConnectionFactory connections) : IAttachmentScanRecoveryStore
{
    public async Task<AttachmentScanRecoveryResult> RecoverPageAsync(Guid organizationId,int maximumRows,CancellationToken ct)
    {
        if(organizationId==Guid.Empty)throw new ArgumentException("Organization ID is required.",nameof(organizationId));
        if(maximumRows is <1 or >32)throw new ArgumentOutOfRangeException(nameof(maximumRows));
        ct.ThrowIfCancellationRequested();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await using var session=await connections.OpenTenantSessionAsync(organizationId,deadline.Token);
        await using(var settings=new NpgsqlCommand("SET LOCAL statement_timeout='1500ms'; SET LOCAL lock_timeout='250ms';",
            session.Connection,session.Transaction))await settings.ExecuteNonQueryAsync(deadline.Token);
        await using var query=new NpgsqlCommand("SELECT visited,recovered FROM public.recover_attachment_scan_page(@tenant,@limit);",session.Connection,session.Transaction);
        query.Parameters.AddWithValue("tenant",organizationId);query.Parameters.AddWithValue("limit",maximumRows);
        AttachmentScanRecoveryResult result;
        await using(var rows=await query.ExecuteReaderAsync(deadline.Token))
        {
            if(!await rows.ReadAsync(deadline.Token))throw new InvalidOperationException("Attachment scan recovery is unavailable.");
            result=new(rows.GetInt32(0),rows.GetInt32(1));
            if(result.Visited<0 || result.Visited>maximumRows || result.Recovered<0 || result.Recovered>result.Visited
                || await rows.ReadAsync(deadline.Token))throw new InvalidOperationException("Attachment scan recovery is unavailable.");
        }
        await session.CommitAsync(deadline.Token);
        return result;
    }
}
