using Npgsql;
using StrataAI.Application.Records;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Records;

internal sealed class PostgresRetentionOrganizationDisposition(PostgresConnectionFactory connections)
    : IRetentionOrganizationDisposition
{
    public async Task<RetentionDispositionAdmission> CheckAsync(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            return RetentionDispositionAdmission.Unavailable;
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM organizations WHERE id=@tenant AND status='ACTIVE'),
              EXISTS(SELECT 1 FROM record_retention_holds WHERE tenant_id=@tenant AND status='ACTIVE');
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || !reader.GetBoolean(0)) return RetentionDispositionAdmission.Unavailable;
        return reader.GetBoolean(1) ? RetentionDispositionAdmission.Held : RetentionDispositionAdmission.Clear;
    }
}
