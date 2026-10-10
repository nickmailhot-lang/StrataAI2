using System.Text.Json;
using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

// Configuration recipient admission belongs to its owning coordinator, which
// checks current Owner/Admin before and after IO. Every persistence operation
// borrows that tenant transaction; no global routing or independent commit.
internal sealed class PostgresOrganizationConfigurationEventReader(PostgresConnectionFactory connections)
    : IOrganizationConfigurationEventReader, IOrganizationConfigurationAttributionReader
{
    public async Task<IReadOnlyList<OrganizationConfigurationAttribution>> ReadAttributionAsync(Guid organization,
        IReadOnlyList<Guid> eventIds, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (eventIds.Count > 51 || eventIds.Any(id => id == Guid.Empty) || eventIds.Distinct().Count() != eventIds.Count)
            throw new ArgumentException("Configuration attribution requires one bounded source window.");
        if (eventIds.Count == 0) return Array.Empty<OrganizationConfigurationAttribution>();
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT event_id,actor_id,version,activity_actor_label,acting_capacity,
              source_membership_id,source_membership_version
            FROM organization_configuration_events
            WHERE tenant_id=@tenant AND event_id=ANY(@events);
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization);
        query.Parameters.AddWithValue("events", eventIds.ToArray());
        List<OrganizationConfigurationAttribution> result = [];
        await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
            result.Add(new(organization, rows.GetGuid(0), rows.GetGuid(1), rows.GetInt64(2), rows.GetString(3), rows.GetString(4),
                rows.IsDBNull(5) ? null : rows.GetGuid(5), rows.IsDBNull(6) ? null : rows.GetInt64(6)));
        return result;
    }

    public async Task<OrganizationConfigurationReadScope?> GetScopeAsync(Guid organization, Guid actor, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT m.id,m.version,m.role FROM organizations o
            JOIN organization_members m ON m.tenant_id=o.id
            WHERE o.id=@tenant AND o.status='ACTIVE' AND m.user_id=@actor
              AND m.status='ACTIVE' AND m.role IN ('OWNER','ADMIN') FOR SHARE OF o,m;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("actor", actor);
        await using var row = await query.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        var role = row.GetString(2) switch
        {
            "OWNER" => OrganizationRole.Owner,
            "ADMIN" => OrganizationRole.Admin,
            _ => throw new InvalidOperationException("Configuration recipient role is unavailable.")
        };
        return new(new(organization, actor, row.GetGuid(0), row.GetInt64(1), role), true, true);
    }

    public async Task<long> GetHeadAsync(Guid organization, CancellationToken ct)
    {
        RequireScope(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("SELECT version FROM organization_configurations WHERE tenant_id=@tenant;",
            session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization);
        return await query.ExecuteScalarAsync(ct) is long version ? version : 0;
    }

    public async Task<OrganizationConfigurationEventBatch> ReadAsync(Guid organization, long since, int limit, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (since < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(since));
        var head = await GetHeadAsync(organization, ct);
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            SELECT event_json::text,ready_at,created_at,event_id,version,actor_id,correlation_id,
              created_at=(event_json->>'CreatedAt')::timestamptz
            FROM organization_configuration_events
            WHERE tenant_id=@tenant AND version>@since AND version<=@head
            ORDER BY version LIMIT @limit;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("since", since);
        query.Parameters.AddWithValue("head", head); query.Parameters.AddWithValue("limit", limit + 1);
        List<OrganizationConfigurationEventCandidate> result = [];
        await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
        {
            var source = JsonSerializer.Deserialize<OrganizationConfigurationEvent>(rows.GetString(0))
                ?? throw new InvalidOperationException("Configuration replay source is unavailable.");
            if (source.OrganizationId != organization || source.EventId != rows.GetGuid(3)
                || source.Version != rows.GetInt64(4) || source.ActorId != rows.GetGuid(5)
                || source.CorrelationId != rows.GetString(6) || !rows.GetBoolean(7))
                throw new InvalidOperationException("Configuration replay source identity or clock is unavailable.");
            result.Add(new(source, rows.IsDBNull(1) ? null : rows.GetFieldValue<DateTimeOffset>(1),
                rows.GetFieldValue<DateTimeOffset>(2)));
        }
        return new(head, result.ToArray());
    }

    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Configuration replay requires its owning tenant transaction.");
    }
}
