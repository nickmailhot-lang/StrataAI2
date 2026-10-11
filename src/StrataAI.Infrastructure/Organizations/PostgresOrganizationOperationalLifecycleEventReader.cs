using System.Text.Json;
using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class PostgresOrganizationOperationalLifecycleEventReader(PostgresConnectionFactory connections,
    OrganizationOperationalLifecycleCommandScope lifecycle) : IOrganizationOperationalLifecycleEventReader
{
    public async Task<OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>> ReadAsync(
        Guid organization, Guid actor, long afterVersion, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!lifecycle.Owns(organization, actor) || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("An owning lifecycle transaction is required.");
        try
        {
            await using var session = await connections.OpenTenantSessionAsync(organization, ct);
            await using var query = new NpgsqlCommand("""
                SELECT h.source_json::text,e.event_id,e.version,e.created_at,e.ready_at
                FROM organization_operational_lifecycle_events e
                JOIN organization_operational_lifecycle_history h USING(tenant_id,version)
                WHERE e.tenant_id=@tenant AND e.version>@after ORDER BY e.version LIMIT 51;
                """, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("after", afterVersion);
            var result = new List<OrganizationOperationalLifecyclePublication>();
            await using var rows = await query.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct))
            {
                var source = JsonSerializer.Deserialize<OrganizationOperationalLifecycleTransition>(rows.GetString(0));
                if (source is null || source.EventId != rows.GetGuid(1) || source.Version != rows.GetInt64(2)
                    || source.CreatedAt != rows.GetFieldValue<DateTimeOffset>(3)) return Failure("organization_lifecycle_source_unavailable");
                result.Add(new(source, rows.IsDBNull(4) ? null : rows.GetFieldValue<DateTimeOffset>(4)));
            }
            return OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>.Success(result);
        }
        catch (JsonException) { return Failure("organization_lifecycle_source_unavailable"); }
        catch (NpgsqlException) { return Failure("organization_storage_unavailable"); }
    }
    private static OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>> Failure(string code) =>
        OrganizationOperation<IReadOnlyList<OrganizationOperationalLifecyclePublication>>.Failure(code);
}
