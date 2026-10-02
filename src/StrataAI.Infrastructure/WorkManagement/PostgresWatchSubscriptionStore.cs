using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresWatchSubscriptionStore(PostgresConnectionFactory connections) : IWatchSubscriptionStore
{
    private const string Columns = "id,tenant_id,user_id,entity_type,entity_id,watching,created_at,updated_at,version";
    public async Task<WatchSubscription?> FindAsync(Guid organizationId, Guid userId, string entityType, Guid entityId, CancellationToken ct)
    {
        await using var session = await connections.OpenTenantSessionAsync(organizationId, ct);
        await using var query = new NpgsqlCommand($"SELECT {Columns} FROM watch_subscriptions WHERE tenant_id=@tenant AND user_id=@user AND entity_type=@type AND entity_id=@entity;", session.Connection, session.Transaction);
        Bind(query, organizationId, userId, entityType, entityId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<WatchSubscription?> SetAsync(Guid organizationId, Guid userId, string entityType, Guid entityId,
        bool watching, long expectedVersion, DateTimeOffset now, CancellationToken ct)
    {
        if (!connections.HasCommandScope(organizationId)) throw new InvalidOperationException("Watch changes require the entity command transaction.");
        await using var session = await connections.OpenTenantSessionAsync(organizationId, ct);
        await using var query = new NpgsqlCommand(expectedVersion == 0 ? $"""
            INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,list_id,card_id,watching,created_at,updated_at,version)
            VALUES(@tenant,@id,@user,@type,@entity,@board,@list,@card,@watching,@now,@now,1)
            ON CONFLICT(tenant_id,user_id,entity_type,entity_id) DO NOTHING RETURNING {Columns};
            """ : $"""
            UPDATE watch_subscriptions SET watching=@watching,updated_at=GREATEST(updated_at,@now),version=version+1
            WHERE tenant_id=@tenant AND user_id=@user AND entity_type=@type AND entity_id=@entity AND version=@version
            RETURNING {Columns};
            """, session.Connection, session.Transaction);
        Bind(query, organizationId, userId, entityType, entityId);
        query.Parameters.AddWithValue("watching", watching); query.Parameters.AddWithValue("now", now);
        if (expectedVersion == 0)
        {
            query.Parameters.AddWithValue("id", Guid.NewGuid());
            foreach (var (name, type) in new[] { ("board", "BOARD"), ("list", "LIST"), ("card", "CARD") })
                query.Parameters.AddWithValue(name, NpgsqlDbType.Uuid, entityType == type ? entityId : DBNull.Value);
        }
        else query.Parameters.AddWithValue("version", expectedVersion);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    private static void Bind(NpgsqlCommand query, Guid organizationId, Guid userId, string type, Guid entityId)
    {
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("user", userId);
        query.Parameters.AddWithValue("type", type); query.Parameters.AddWithValue("entity", entityId);
    }
    private static WatchSubscription Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetString(3), reader.GetGuid(4), reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetInt64(8));
}
