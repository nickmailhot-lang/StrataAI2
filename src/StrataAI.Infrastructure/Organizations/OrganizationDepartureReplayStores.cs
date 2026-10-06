using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationDepartureReplayStore : IOrganizationDepartureReplayStore, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid, Guid, Guid), OrganizationDepartureReplay> _records = [];
    public Task<OrganizationDepartureReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken)
        => Task.FromResult(_records.GetValueOrDefault((organizationId, actorId, key)));
    public Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationDepartureReplay replay, CancellationToken cancellationToken)
    { _records.Add((organizationId, actorId, key), replay); return Task.CompletedTask; }
    public Action CaptureRollback()
    {
        var snapshot = _records.ToArray();
        return () => { _records.Clear(); foreach (var entry in snapshot) _records.Add(entry.Key, entry.Value); };
    }
}

internal sealed class PostgresOrganizationDepartureReplayStore(PostgresConnectionFactory connections) : IOrganizationDepartureReplayStore
{
    public async Task<OrganizationDepartureReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT expires_at FROM organization_departure_replays
            WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("key", key);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        return await rows.ReadAsync(cancellationToken) ? new(rows.GetFieldValue<DateTimeOffset>(0)) : null;
    }
    public async Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationDepartureReplay replay, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO organization_departure_replays(tenant_id,actor_id,key_id,expires_at)
            VALUES(@tenant,@actor,@key,@expires);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("key", key); command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private void RequireScope(Guid organizationId)
    {
        if (!connections.HasCommandScope(organizationId)) throw new InvalidOperationException("Departure retries require an owning transaction.");
    }
}
