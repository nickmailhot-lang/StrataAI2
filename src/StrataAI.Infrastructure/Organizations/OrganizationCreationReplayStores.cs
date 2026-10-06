using System.Text.Json;
using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationCreationReplayStore : IOrganizationCreationReplayStore, IDemoOrganizationTransactionParticipant
{
    private readonly Dictionary<(Guid, Guid, Guid), OrganizationCreationReplay> _records = [];
    public Task<OrganizationCreationReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken)
        => Task.FromResult(_records.GetValueOrDefault((organizationId, actorId, key)));
    public Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationCreationReplay replay, CancellationToken cancellationToken)
    {
        _records.Add((organizationId, actorId, key), replay);
        return Task.CompletedTask;
    }
    public Action CaptureRollback()
    {
        var snapshot = _records.ToArray();
        return () => { _records.Clear(); foreach (var entry in snapshot) _records.Add(entry.Key, entry.Value); };
    }
}

internal sealed class PostgresOrganizationCreationReplayStore(PostgresConnectionFactory connections) : IOrganizationCreationReplayStore
{
    public async Task<OrganizationCreationReplay?> ReadAsync(Guid organizationId, Guid actorId, Guid key, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT fingerprint,result_json::text,expires_at FROM organization_creation_replays
            WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId);
        command.Parameters.AddWithValue("actor", actorId); command.Parameters.AddWithValue("key", key);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        if (!await rows.ReadAsync(cancellationToken)) return null;
        var result = JsonSerializer.Deserialize<OrganizationSummary>(rows.GetString(1))
            ?? throw new InvalidOperationException("Invalid Organization acknowledgment.");
        if (result.Organization.Id != organizationId || result.Organization.Version < 1)
            throw new InvalidOperationException("Invalid Organization acknowledgment scope.");
        return new(rows.GetString(0), result, rows.GetFieldValue<DateTimeOffset>(2));
    }
    public async Task SaveAsync(Guid organizationId, Guid actorId, Guid key, OrganizationCreationReplay replay, CancellationToken cancellationToken)
    {
        RequireScope(organizationId);
        if (replay.Result.Organization.Id != organizationId) throw new InvalidOperationException("Invalid Organization acknowledgment scope.");
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO organization_creation_replays(tenant_id,actor_id,key_id,fingerprint,result_json,expires_at)
            VALUES(@tenant,@actor,@key,@fingerprint,@result::jsonb,@expires);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("key", key); command.Parameters.AddWithValue("fingerprint", replay.Fingerprint);
        command.Parameters.AddWithValue("result", JsonSerializer.Serialize(replay.Result));
        command.Parameters.AddWithValue("expires", replay.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private void RequireScope(Guid organizationId)
    {
        if (!connections.HasCommandScope(organizationId))
            throw new InvalidOperationException("Organization retries require an owning transaction.");
    }
}
