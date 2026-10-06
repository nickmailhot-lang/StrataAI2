using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationMetadataEventReader(PostgresConnectionFactory connections) : IOrganizationMetadataEventReader
{
    private void RequireScope(Guid organizationId)
    {
        if (!connections.HasCommandScope(organizationId)) throw new InvalidOperationException("Metadata replay requires its owning tenant read transaction.");
    }
    public async Task<OrganizationMetadataCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT m.id,m.version FROM organizations o JOIN organization_members m ON m.tenant_id=o.id
             WHERE o.id=@tenant AND o.status='ACTIVE' AND m.user_id=@actor AND m.status='ACTIVE' FOR SHARE OF o,m;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("actor", actorId);
        OrganizationMetadataCursorBinding? result = null;
        await using (var row = await query.ExecuteReaderAsync(cancellationToken))
            if (await row.ReadAsync(cancellationToken)) result = new(organizationId, actorId, row.GetGuid(0), row.GetInt64(1));
        await session.CommitAsync(cancellationToken); return result;
    }
    public async Task<long> GetHeadAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var query = new NpgsqlCommand("SELECT last_sequence FROM organization_metadata_event_streams WHERE tenant_id=@tenant", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId);
        var head = await query.ExecuteScalarAsync(cancellationToken) is long value ? value : 0;
        await session.CommitAsync(cancellationToken); return head;
    }
    public async Task<OrganizationMetadataEventWindow> ReadAsync(Guid organizationId, long since, int limit, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        if (since < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(since));
        var head = await GetHeadAsync(organizationId, cancellationToken);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT sequence,event_id,event_type,actor_id,entity_version,created_at,ready_at IS NOT NULL,entity_type,entity_id
             FROM organization_metadata_events WHERE tenant_id=@tenant AND sequence>@since AND sequence<=@head
             ORDER BY sequence LIMIT @limit;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("since", since);
        query.Parameters.AddWithValue("head", head); query.Parameters.AddWithValue("limit", limit + 1);
        List<OrganizationMetadataEventCandidate> rows = [];
        await using (var row = await query.ExecuteReaderAsync(cancellationToken))
            while (await row.ReadAsync(cancellationToken)) rows.Add(new(row.GetInt64(0), new(row.GetGuid(1), row.GetString(2), row.GetGuid(3),
                organizationId, row.GetInt64(4), row.GetFieldValue<DateTimeOffset>(5), row.GetString(7), row.GetGuid(8)), row.GetBoolean(6)));
        await session.CommitAsync(cancellationToken); return OrganizationMetadataEventWindow.Build(since, head, limit, rows);
    }
}
