using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    private const string LabelColumns = "id,tenant_id,board_id,name,color,rank,status,created_at,updated_at,version";
    private static BoardLabelRecord ReadLabel(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6) == "DELETED",
        reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetInt64(9));

    public async Task<BoardLabelRecord?> FindLabelAsync(Guid labelId, CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        Guid tenant;
        await using (var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken))
        {
            await routing.SetLookupAsync(RoutingLookup.Label, labelId.ToString(), cancellationToken);
            await using var lookup = new NpgsqlCommand("SELECT tenant_id FROM label_routes WHERE label_id=@id AND (@deleted OR status='ACTIVE');", routing.Connection, routing.Transaction);
            lookup.Parameters.AddWithValue("id", labelId); lookup.Parameters.AddWithValue("deleted", includeDeleted);
            if (await lookup.ExecuteScalarAsync(cancellationToken) is not Guid found) return null;
            tenant = found;
        }
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenant, cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {LabelColumns} FROM board_labels WHERE tenant_id=@tenant AND id=@id AND (@deleted OR status='ACTIVE');", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("id", labelId); command.Parameters.AddWithValue("deleted", includeDeleted);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLabel(reader) : null;
    }

    public async Task<IReadOnlyList<BoardLabelRecord>> ListLabelsAsync(Guid boardId, Guid? after, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken);
        if (board is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {LabelColumns} FROM board_labels WHERE tenant_id=@tenant AND board_id=@board AND status='ACTIVE' AND (@after IS NULL OR id>@after) ORDER BY id LIMIT 51;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", board.OrganizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<BoardLabelRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadLabel(reader));
        return result;
    }

    public async Task<BoardLabelRecord> CreateLabelAsync(Guid boardId, Guid id, string name, string color, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken) ?? throw new InvalidOperationException("Label Board was not found.");
        if (!connectionFactory.HasCommandScope(board.OrganizationId)) throw new InvalidOperationException("Label creation requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        string rank;
        await using (var last = new NpgsqlCommand("SELECT rank FROM board_labels WHERE tenant_id=@tenant AND board_id=@board AND status='ACTIVE' ORDER BY rank DESC,id DESC LIMIT 1;", session.Connection, session.Transaction))
        {
            last.Parameters.AddWithValue("tenant", board.OrganizationId); last.Parameters.AddWithValue("board", boardId);
            rank = RankToken.After(await last.ExecuteScalarAsync(cancellationToken) as string);
        }
        await using var command = new NpgsqlCommand($"INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,created_at,updated_at) VALUES(@id,@tenant,@board,@name,@color,@rank,@now,@now) RETURNING {LabelColumns};", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("tenant", board.OrganizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("name", name); command.Parameters.AddWithValue("color", color); command.Parameters.AddWithValue("rank", rank); command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Label creation did not return a record.");
        return ReadLabel(reader);
    }

    public Task<BoardLabelRecord?> UpdateLabelAsync(Guid labelId, string name, string color, string rank, long version, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        MutateLabelAsync(labelId, version, now, false, name, color, rank, cancellationToken);
    public Task<BoardLabelRecord?> DeleteLabelAsync(Guid labelId, long version, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        MutateLabelAsync(labelId, version, now, true, "", "", "", cancellationToken);

    private async Task<BoardLabelRecord?> MutateLabelAsync(Guid id, long version, DateTimeOffset now, bool deleting, string name, string color, string rank, CancellationToken ct)
    {
        var label = await FindLabelAsync(id, ct);
        if (label is null) return null;
        if (!connectionFactory.HasCommandScope(label.OrganizationId)) throw new InvalidOperationException("Label mutation requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(label.OrganizationId, ct);
        BoardLabelRecord? result;
        await using (var command = new NpgsqlCommand($"UPDATE board_labels SET {(deleting ? "status='DELETED',deleted_at=@now" : "name=@name,color=@color,rank=@rank")},updated_at=@now,version=version+1 WHERE tenant_id=@tenant AND id=@id AND status='ACTIVE' AND version=@version RETURNING {LabelColumns};", session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("tenant", label.OrganizationId); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("now", now);
            if (!deleting) { command.Parameters.AddWithValue("name", name); command.Parameters.AddWithValue("color", color); command.Parameters.AddWithValue("rank", rank); }
            await using var reader = await command.ExecuteReaderAsync(ct);
            result = await reader.ReadAsync(ct) ? ReadLabel(reader) : null;
        }
        if (deleting && result is not null)
        {
            await using var remove = new NpgsqlCommand("WITH removed AS (DELETE FROM card_labels WHERE tenant_id=@tenant AND board_id=@board AND label_id=@id RETURNING card_id) UPDATE cards SET version=version+1,updated_at=@now WHERE tenant_id=@tenant AND board_id=@board AND lifecycle_state<>'DELETED' AND id IN (SELECT card_id FROM removed);", session.Connection, session.Transaction);
            remove.Parameters.AddWithValue("tenant", label.OrganizationId); remove.Parameters.AddWithValue("board", label.BoardId); remove.Parameters.AddWithValue("id", id); remove.Parameters.AddWithValue("now", now);
            await remove.ExecuteNonQueryAsync(ct);
        }
        return result;
    }
}
