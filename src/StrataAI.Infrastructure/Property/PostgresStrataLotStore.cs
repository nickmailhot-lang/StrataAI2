using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Property;
using StrataAI.Domain.Property;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Property;

internal sealed class PostgresStrataLotStore(PostgresConnectionFactory connections) : IStrataLotStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<TenantDbSession> Session(Guid organization, CancellationToken ct) {
        if (!connections.HasCommandScope(organization)) throw new InvalidOperationException("Unit directory requires its Organization transaction.");
        return await connections.OpenTenantSessionAsync(organization, ct);
    }
    public async Task<IReadOnlyList<StrataLot>> ListAsync(Guid organization, Guid? after, CancellationToken ct) {
        await using var session = await Session(organization, ct);
        await using var command = new NpgsqlCommand("SELECT payload FROM strata_lots WHERE tenant_id=@tenant AND (@after IS NULL OR id>@after) ORDER BY id LIMIT 51;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var rows = new List<StrataLot>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(JsonSerializer.Deserialize<StrataLot>(reader.GetString(0), Json)!);
        return rows;
    }
    public async Task<StrataLot?> ReadAsync(Guid organization, Guid id, CancellationToken ct) {
        await using var session = await Session(organization, ct);
        await using var command = new NpgsqlCommand("SELECT payload FROM strata_lots WHERE tenant_id=@tenant AND id=@id;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("id", id);
        var value = await command.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<StrataLot>(json, Json) : null;
    }
    public async Task<IReadOnlyList<StrataLotRevision>> HistoryAsync(Guid organization, Guid id, long? before, CancellationToken ct) {
        await using var session = await Session(organization, ct);
        await using var command = new NpgsqlCommand("SELECT payload FROM strata_lot_history WHERE tenant_id=@tenant AND lot_id=@id AND (@before IS NULL OR version<@before) ORDER BY version DESC LIMIT 51;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("before", NpgsqlDbType.Bigint, (object?)before ?? DBNull.Value);
        var rows = new List<StrataLotRevision>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(JsonSerializer.Deserialize<StrataLotRevision>(reader.GetString(0), Json)!);
        return rows;
    }
    public async Task<StrataLotReceipt?> ReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) {
        await using var session = await Session(organization, ct);
        await using var command = new NpgsqlCommand("SELECT payload FROM strata_lot_receipts WHERE tenant_id=@tenant AND actor_id=@actor AND key_id=@key;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("key", key);
        var value = await command.ExecuteScalarAsync(ct);
        return value is string json ? JsonSerializer.Deserialize<StrataLotReceipt>(json, Json) : null;
    }
    public async Task<StrataLotWriteResult> WriteAsync(StrataLotRevision revision, StrataLotReceipt receipt, long expectedVersion, CancellationToken ct) {
        var lot = revision.Lot; await using var session = await Session(lot.OrganizationId, ct);
        await using var savepoint = new NpgsqlCommand("SAVEPOINT strata_lot_write;", session.Connection, session.Transaction);
        await savepoint.ExecuteNonQueryAsync(ct);
        try {
            var sql = expectedVersion == 0 ? """
                INSERT INTO strata_lots(tenant_id,id,version,unit_identifier,lot_identifier,payload)
                VALUES(@tenant,@id,@next,@unit,@lot,@payload);
                """ : """
                UPDATE strata_lots SET version=@next,unit_identifier=@unit,lot_identifier=@lot,payload=@payload
                WHERE tenant_id=@tenant AND id=@id AND version=@expected;
                """;
            await using var current = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            current.Parameters.AddWithValue("tenant", lot.OrganizationId); current.Parameters.AddWithValue("id", lot.Id);
            current.Parameters.AddWithValue("next", lot.Version); current.Parameters.AddWithValue("expected", expectedVersion);
            current.Parameters.AddWithValue("unit", lot.Data.UnitIdentifier); current.Parameters.AddWithValue("lot", lot.Data.LotIdentifier);
            current.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(lot, Json));
            if (await current.ExecuteNonQueryAsync(ct) != 1) return StrataLotWriteResult.VersionConflict;
            await using var history = new NpgsqlCommand("""
                INSERT INTO strata_lot_history(tenant_id,lot_id,version,event_id,payload) VALUES(@tenant,@id,@version,@event,@history);
                INSERT INTO strata_lot_receipts(tenant_id,actor_id,key_id,lot_id,version,payload) VALUES(@tenant,@actor,@key,@id,@version,@receipt);
                """, session.Connection, session.Transaction);
            history.Parameters.AddWithValue("tenant", lot.OrganizationId); history.Parameters.AddWithValue("id", lot.Id);
            history.Parameters.AddWithValue("version", lot.Version); history.Parameters.AddWithValue("event", revision.EventId);
            history.Parameters.AddWithValue("actor", receipt.ActorId); history.Parameters.AddWithValue("key", receipt.Key);
            history.Parameters.AddWithValue("history", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(revision, Json));
            history.Parameters.AddWithValue("receipt", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(receipt, Json));
            await history.ExecuteNonQueryAsync(ct);
            return StrataLotWriteResult.Written;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation) {
            await using var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT strata_lot_write;", session.Connection, session.Transaction);
            await rollback.ExecuteNonQueryAsync(ct);
            return error.ConstraintName is "strata_lots_unit_key" or "strata_lots_lot_key"
                ? StrataLotWriteResult.IdentifierConflict : StrataLotWriteResult.VersionConflict;
        }
    }
}
