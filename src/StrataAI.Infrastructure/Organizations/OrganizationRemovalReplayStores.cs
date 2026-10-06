using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationRemovalReplayStore : IOrganizationRemovalReplayStore, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid, Guid, Guid), OrganizationRemovalReplay> _records = [];
    public Task<OrganizationRemovalReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken)
        => Task.FromResult(_records.GetValueOrDefault((organizationId, actorId, key)));
    public Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationRemovalReplay replay, CancellationToken cancellationToken)
    { _records.Add((organizationId, actorId, key), replay); return Task.CompletedTask; }
    public Action CaptureRollback()
    {
        var snapshot = _records.ToArray();
        return () => { _records.Clear(); foreach (var entry in snapshot) _records.Add(entry.Key, entry.Value); };
    }
}

internal sealed class PostgresOrganizationRemovalReplayStore(PostgresConnectionFactory connections) : IOrganizationRemovalReplayStore
{
    public async Task<OrganizationRemovalReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT fingerprint,expires_at FROM organization_removal_replays
            WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("key", key);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        return await rows.ReadAsync(cancellationToken) ? new(rows.GetString(0), rows.GetFieldValue<DateTimeOffset>(1)) : null;
    }
    public async Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationRemovalReplay replay, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO organization_removal_replays(tenant_id,actor_id,key_id,fingerprint,expires_at)
            VALUES(@tenant,@actor,@key,@fingerprint,@expires);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("key", key); command.Parameters.AddWithValue("fingerprint", replay.Fingerprint); command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private void RequireScope(Guid organizationId)
    {
        if (!connections.HasCommandScope(organizationId)) throw new InvalidOperationException("Removal retries require an owning transaction.");
    }
}
