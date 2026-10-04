using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardLabelOption>> ListCardLabelOptionsAsync(Guid cardId, Guid? after, CancellationToken cancellationToken = default)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        if (card is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(card.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {LabelColumns},EXISTS(SELECT 1 FROM card_labels a WHERE a.tenant_id=label.tenant_id AND a.board_id=label.board_id AND a.label_id=label.id AND a.card_id=@card) FROM board_labels label WHERE tenant_id=@tenant AND board_id=@board AND status='ACTIVE' AND (@after IS NULL OR id>@after) ORDER BY id LIMIT 51;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", card.OrganizationId); command.Parameters.AddWithValue("board", card.BoardId); command.Parameters.AddWithValue("card", cardId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardLabelOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(ReadLabel(reader), reader.GetBoolean(10)));
        return result;
    }
    public async Task<BoardLabelRecord?> MoveLabelAsync(Guid labelId, Guid? beforeLabelId, long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var label = await FindLabelAsync(labelId, cancellationToken);
        if (label is null || label.Version != version) return null;
        if (!connectionFactory.HasCommandScope(label.OrganizationId)) throw new InvalidOperationException("Label ordering requires the owning command transaction.");
        var anchor = beforeLabelId is null ? null : await FindLabelAsync(beforeLabelId.Value, cancellationToken);
        if (beforeLabelId is not null && (anchor is null || anchor.BoardId != label.BoardId || anchor.OrganizationId != label.OrganizationId || anchor.Id == label.Id)) return null;
        await using var session = await connectionFactory.OpenTenantSessionAsync(label.OrganizationId, cancellationToken);
        await using var previous = new NpgsqlCommand("SELECT rank FROM board_labels WHERE tenant_id=@tenant AND board_id=@board AND status='ACTIVE' AND id<>@id AND (@before IS NULL OR rank<@upper OR (rank=@upper AND id<@before)) ORDER BY rank DESC,id DESC LIMIT 1;", session.Connection, session.Transaction);
        previous.Parameters.AddWithValue("tenant", label.OrganizationId); previous.Parameters.AddWithValue("board", label.BoardId); previous.Parameters.AddWithValue("id", labelId);
        previous.Parameters.AddWithValue("before", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)beforeLabelId ?? DBNull.Value);
        previous.Parameters.AddWithValue("upper", NpgsqlTypes.NpgsqlDbType.Text, (object?)anchor?.Rank ?? DBNull.Value);
        var lower = await previous.ExecuteScalarAsync(cancellationToken) as string;
        if (anchor is not null && lower == anchor.Rank) throw new RankSpaceExhaustedException();
        var rank = anchor is null ? RankToken.After(lower) : RankToken.Between(lower, anchor.Rank);
        return await UpdateLabelAsync(labelId, label.Name, label.Color, rank, version, now, cancellationToken);
    }
    private static async Task<IReadOnlyDictionary<Guid, CardLabelPreview>> LoadLabelPreviewsAsync(TenantDbSession session, Guid tenant, Guid board, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT card_id,id,name,color,total FROM (
              SELECT a.card_id,l.id,l.name,l.color,count(*) OVER(PARTITION BY a.card_id) AS total,
                row_number() OVER(PARTITION BY a.card_id ORDER BY l.rank,l.id) AS position
              FROM card_labels a JOIN board_labels l ON l.tenant_id=a.tenant_id AND l.board_id=a.board_id AND l.id=a.label_id
              JOIN cards c ON c.tenant_id=a.tenant_id AND c.board_id=a.board_id AND c.id=a.card_id
              JOIN board_lists parent ON parent.tenant_id=c.tenant_id AND parent.board_id=c.board_id AND parent.id=c.list_id
              WHERE a.tenant_id=@tenant AND a.board_id=@board AND l.status='ACTIVE'
                AND c.lifecycle_state='ACTIVE' AND parent.lifecycle_state='ACTIVE'
            ) ranked WHERE position<=6 ORDER BY card_id,position;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenant); command.Parameters.AddWithValue("board", board);
        var result = new Dictionary<Guid, CardLabelPreview>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!result.TryGetValue(id, out var preview)) result[id] = preview = new(new List<CardLabelIndicator>(), reader.GetInt64(4));
            ((List<CardLabelIndicator>)preview.Items).Add(new(reader.GetGuid(1), reader.GetString(2), reader.GetString(3)));
        }
        return result;
    }
    private async Task<Dictionary<Guid, Guid>> CopyListLabelDefinitionsAsync(Guid organizationId, Guid sourceBoardId,
        Guid sourceListId, Guid destinationBoardId, DateTimeOffset now, CancellationToken ct)
    {
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, ct);
        var labels = new List<BoardLabelRecord>();
        await using (var command = new NpgsqlCommand($"SELECT {string.Join(',', LabelColumns.Split(',').Select(column => "label." + column))} FROM board_labels label WHERE label.tenant_id=@tenant AND label.board_id=@board AND label.status='ACTIVE' AND EXISTS(SELECT 1 FROM card_labels a JOIN cards c ON c.id=a.card_id AND c.tenant_id=a.tenant_id AND c.board_id=a.board_id WHERE a.tenant_id=label.tenant_id AND a.board_id=label.board_id AND a.label_id=label.id AND c.list_id=@list AND c.lifecycle_state<>'DELETED') ORDER BY label.rank,label.id;", session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("board", sourceBoardId); command.Parameters.AddWithValue("list", sourceListId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) labels.Add(ReadLabel(reader));
        }
        var mapping = new Dictionary<Guid, Guid>();
        foreach (var label in labels)
        {
            var target = sourceBoardId == destinationBoardId ? label : await CreateLabelAsync(destinationBoardId, Guid.NewGuid(), label.Name, label.Color, now, ct);
            mapping.Add(label.Id, target.Id);
        }
        return mapping;
    }
    public async Task<IReadOnlyList<BoardLabelRecord>> ListCardLabelsAsync(Guid cardId, Guid? after, CancellationToken cancellationToken = default)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        if (card is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(card.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {LabelColumns} FROM board_labels label WHERE tenant_id=@tenant AND board_id=@board AND status='ACTIVE' AND (@after IS NULL OR id>@after) AND EXISTS(SELECT 1 FROM card_labels a WHERE a.tenant_id=label.tenant_id AND a.board_id=label.board_id AND a.label_id=label.id AND a.card_id=@card) ORDER BY id LIMIT 51;", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", card.OrganizationId); command.Parameters.AddWithValue("board", card.BoardId); command.Parameters.AddWithValue("card", cardId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<BoardLabelRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadLabel(reader));
        return result;
    }
    public async Task<CardLabelChange?> SetCardLabelAsync(Guid cardId, Guid labelId, bool assigned, long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var card = await FindCardAsync(cardId, cancellationToken);
        if (card is null) return null;
        if (!connectionFactory.HasCommandScope(card.OrganizationId)) throw new InvalidOperationException("Card label assignment requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(card.OrganizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", card.OrganizationId); command.Parameters.AddWithValue("board", card.BoardId);
            command.Parameters.AddWithValue("card", cardId); command.Parameters.AddWithValue("label", labelId);
            command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("now", now);
            return command;
        }
        // The Board gate is already held. Lock the current Card and reject stale
        // revisions before touching an association, even for an idempotent no-op.
        await using (var current = Query("SELECT version FROM cards WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND lifecycle_state='ACTIVE' FOR UPDATE;"))
            if (await current.ExecuteScalarAsync(cancellationToken) is not long currentVersion || currentVersion != version) return null;
        await using (var currentLabel = Query("SELECT id FROM board_labels WHERE tenant_id=@tenant AND board_id=@board AND id=@label AND status='ACTIVE';"))
            if (await currentLabel.ExecuteScalarAsync(cancellationToken) is null) return null;
        int count;
        await using (var change = Query(assigned
            ? "INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at) VALUES(@tenant,@board,@card,@label,@now,@now) ON CONFLICT(tenant_id,card_id,label_id) DO NOTHING;"
            : "DELETE FROM card_labels WHERE tenant_id=@tenant AND board_id=@board AND card_id=@card AND label_id=@label;"))
            count = await change.ExecuteNonQueryAsync(cancellationToken);
        if (count > 0)
        {
            await using var update = Query("UPDATE cards SET version=version+1,updated_at=@now WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND version=@version RETURNING id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,version, start_at, due_at, due_timezone, due_has_time, due_complete, archived_at, deleted_at;");
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Locked Card revision changed unexpectedly.");
            card = ReadCard(reader);
        }
        return new(card, labelId, assigned, count > 0);
    }
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
