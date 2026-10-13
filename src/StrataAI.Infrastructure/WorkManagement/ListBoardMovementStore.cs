using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore : IListBoardMovementStore
{
    private sealed record ListMovingLabel(Guid Card, Guid Label, DateTimeOffset CreatedAt, long Version);
    private sealed record ListMovingMember(Guid Card, Guid User, Guid AssignedBy, DateTimeOffset CreatedAt, long Version);

    public async Task<ListBoardTransfer?> TransferListBoardAsync(Guid organization, Guid sourceBoard, Guid list,
        Guid destinationBoard, long expectedVersion, DateTimeOffset now, bool requireVerifiedEmail,
        Guid? beforeList, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization))
            throw new InvalidOperationException("List transfer requires its owning command transaction.");
        if (sourceBoard == destinationBoard || sourceBoard == Guid.Empty || destinationBoard == Guid.Empty) return null;
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        // Hold both parent gates in the same deterministic order as commands.
        await using (var parents = new NpgsqlCommand("SELECT id FROM boards WHERE tenant_id=@tenant AND id=ANY(@boards) AND lifecycle_state='ACTIVE' ORDER BY id FOR UPDATE;", session.Connection, session.Transaction))
        {
            parents.Parameters.AddWithValue("tenant", organization);
            parents.Parameters.AddWithValue("boards", new[] { sourceBoard, destinationBoard });
            await using var rows = await parents.ExecuteReaderAsync(ct); var count = 0;
            while (await rows.ReadAsync(ct)) count++;
            if (count != 2) return null;
        }
        await using (var parent = new NpgsqlCommand("SELECT id FROM board_lists WHERE tenant_id=@tenant AND board_id=@source AND id=@list FOR UPDATE;", session.Connection, session.Transaction))
        {
            parent.Parameters.AddWithValue("tenant", organization); parent.Parameters.AddWithValue("source", sourceBoard); parent.Parameters.AddWithValue("list", list);
            if (await parent.ExecuteScalarAsync(ct) is null) return null;
        }
        var current = await FindListAsync(list, ct);
        if (current is null || current.OrganizationId != organization || current.BoardId != sourceBoard
            || current.LifecycleState != WorkItemLifecycleState.Active || current.Version != expectedVersion || now < current.UpdatedAt) return null;
        // The owning configuration must resolve a live intake binding. Never
        // silently rewrite protected configuration as a side effect of movement.
        await using (var intake = new NpgsqlCommand("SELECT 1 FROM organization_configurations WHERE tenant_id=@tenant AND intake_list_id=@list;", session.Connection, session.Transaction))
        {
            intake.Parameters.AddWithValue("tenant", organization); intake.Parameters.AddWithValue("list", list);
            if (await intake.ExecuteScalarAsync(ct) is not null) return null;
        }
        var rank = await AllocateListPositionAsync(session.Connection, session.Transaction, organization, destinationBoard, list, beforeList, ct);
        if (rank is null) return null;
        var cards = new List<ListTransferredCard>();
        await using (var query = new NpgsqlCommand("SELECT id,version,lifecycle_state,updated_at FROM cards WHERE tenant_id=@tenant AND board_id=@source AND list_id=@list ORDER BY id FOR UPDATE;", session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("source", sourceBoard); query.Parameters.AddWithValue("list", list);
            await using var rows = await query.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct))
            {
                if (now < rows.GetFieldValue<DateTimeOffset>(3)) return null;
                cards.Add(new(rows.GetGuid(0), rows.GetInt64(1), ParseWorkLifecycle(rows.GetString(2))));
            }
        }
        var labels = new List<BoardLabelRecord>(); var associations = new List<ListMovingLabel>(); var members = new List<ListMovingMember>();
        await using (var query = new NpgsqlCommand($"SELECT DISTINCT {string.Join(',', LabelColumns.Split(',').Select(c => "l." + c))} FROM board_labels l JOIN card_labels a ON a.tenant_id=l.tenant_id AND a.board_id=l.board_id AND a.label_id=l.id JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE c.tenant_id=@tenant AND c.board_id=@source AND c.list_id=@list;", session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("source", sourceBoard); query.Parameters.AddWithValue("list", list);
            await using var rows = await query.ExecuteReaderAsync(ct); while (await rows.ReadAsync(ct)) labels.Add(ReadLabel(rows));
        }
        await using (var query = new NpgsqlCommand("SELECT a.card_id,a.label_id,a.created_at,a.version FROM card_labels a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE c.tenant_id=@tenant AND c.board_id=@source AND c.list_id=@list;", session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("source", sourceBoard); query.Parameters.AddWithValue("list", list);
            await using var rows = await query.ExecuteReaderAsync(ct); while (await rows.ReadAsync(ct)) associations.Add(new(rows.GetGuid(0), rows.GetGuid(1), rows.GetFieldValue<DateTimeOffset>(2), rows.GetInt64(3)));
        }
        await using (var query = new NpgsqlCommand("""
            SELECT a.card_id,a.user_id,a.assigned_by,a.created_at,a.version
            FROM card_members a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id
            JOIN board_members b ON b.tenant_id=a.tenant_id AND b.user_id=a.user_id AND b.board_id=@destination
            JOIN organization_members o ON o.tenant_id=a.tenant_id AND o.user_id=a.user_id JOIN users u ON u.id=a.user_id
            WHERE c.tenant_id=@tenant AND c.board_id=@source AND c.list_id=@list
              AND b.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified);
            """, session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("source", sourceBoard); query.Parameters.AddWithValue("list", list);
            query.Parameters.AddWithValue("destination", destinationBoard); query.Parameters.AddWithValue("verified", requireVerifiedEmail);
            await using var rows = await query.ExecuteReaderAsync(ct); while (await rows.ReadAsync(ct)) members.Add(new(rows.GetGuid(0), rows.GetGuid(1), rows.GetGuid(2), rows.GetFieldValue<DateTimeOffset>(3), rows.GetInt64(4)));
        }
        var plan = CardLabelTransferPlan.Create(organization, sourceBoard, cards.Select(c => c.Id).ToArray(), labels,
            associations.Select(a => new CardLabelTransferAssociation(a.Card, a.Label)));
        var mapped = new Dictionary<Guid, Guid>(); var definitions = new List<BoardLabelRecord>(); string? lastRank = null;
        if (plan.Labels.Count > 0)
        {
            await using var last = new NpgsqlCommand("SELECT rank FROM board_labels WHERE tenant_id=@tenant AND board_id=@destination AND status='ACTIVE' ORDER BY rank DESC,id DESC LIMIT 1;", session.Connection, session.Transaction);
            last.Parameters.AddWithValue("tenant", organization); last.Parameters.AddWithValue("destination", destinationBoard);
            lastRank = await last.ExecuteScalarAsync(ct) as string;
        }
        // Exhaustion and version arithmetic must finish before the first write.
        // Definition insertion and reattachment are set-based even for large graphs.
        foreach (var label in plan.Labels)
        {
            lastRank = RankToken.After(lastRank); var id = Guid.NewGuid(); mapped.Add(label.Id, id);
            definitions.Add(label with { Id = id, BoardId = destinationBoard, Rank = lastRank, CreatedAt = now, UpdatedAt = now, Version = 1 });
        }
        var definitionJson = JsonSerializer.Serialize(definitions.Select(l => new { id = l.Id, name = l.Name, color = l.Color, rank = l.Rank }));
        var labelJson = JsonSerializer.Serialize(associations.Where(a => mapped.ContainsKey(a.Label))
            .Select(a => new { card_id = a.Card, label_id = mapped[a.Label], created_at = a.CreatedAt, version = checked(a.Version + 1) }));
        var memberJson = JsonSerializer.Serialize(members.Select(m => new { card_id = m.Card, user_id = m.User, assigned_by = m.AssignedBy, created_at = m.CreatedAt, version = checked(m.Version + 1) }));
        await using var planner = new NpgsqlCommand("SELECT current_setting('enable_seqscan');", session.Connection, session.Transaction);
        var priorPlanner = (string)(await planner.ExecuteScalarAsync(ct))!;
        await using var move = new NpgsqlCommand("""
            -- Earlier small writes can cache sequential RI probes. Replan the
            -- point-key integrity checks before a large reparenting operation.
            SELECT set_config('enable_seqscan','off',true);
            DISCARD PLANS;
            INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,status,created_at,updated_at,version)
              SELECT l.id,@tenant,@destination,l.name,l.color,l.rank,'ACTIVE',@now,@now,1
              FROM jsonb_to_recordset(@definitions) AS l(id uuid,name text,color text,rank text);
            SET CONSTRAINTS cards_list_id_board_id_tenant_id_fkey DEFERRED;
            DELETE FROM card_labels a USING cards c WHERE a.tenant_id=c.tenant_id AND a.card_id=c.id
              AND c.tenant_id=@tenant AND c.board_id=@source AND c.list_id=@list;
            DELETE FROM card_members a USING cards c WHERE a.tenant_id=c.tenant_id AND a.card_id=c.id
              AND c.tenant_id=@tenant AND c.board_id=@source AND c.list_id=@list;
            UPDATE board_lists SET board_id=@destination,rank=@rank,updated_at=@now,version=version+1
              WHERE tenant_id=@tenant AND board_id=@source AND id=@list AND version=@version AND lifecycle_state='ACTIVE';
            UPDATE cards SET board_id=@destination,updated_at=@now,version=version+1
              WHERE tenant_id=@tenant AND board_id=@source AND list_id=@list;
            INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at,version)
              SELECT @tenant,@destination,a.card_id,a.label_id,a.created_at,@now,a.version
              FROM jsonb_to_recordset(@labels) AS a(card_id uuid,label_id uuid,created_at timestamptz,version bigint);
            INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by,created_at,updated_at,version)
              SELECT @tenant,@destination,a.card_id,a.user_id,a.assigned_by,a.created_at,@now,a.version
              FROM jsonb_to_recordset(@members) AS a(card_id uuid,user_id uuid,assigned_by uuid,created_at timestamptz,version bigint);
            SET CONSTRAINTS cards_list_id_board_id_tenant_id_fkey IMMEDIATE;
            SELECT set_config('enable_seqscan',@prior_planner,true);
            """, session.Connection, session.Transaction);
        move.Parameters.AddWithValue("tenant", organization); move.Parameters.AddWithValue("source", sourceBoard); move.Parameters.AddWithValue("destination", destinationBoard);
        move.Parameters.AddWithValue("list", list); move.Parameters.AddWithValue("version", expectedVersion); move.Parameters.AddWithValue("rank", rank); move.Parameters.AddWithValue("now", now);
        move.Parameters.AddWithValue("labels", NpgsqlDbType.Jsonb, labelJson); move.Parameters.AddWithValue("members", NpgsqlDbType.Jsonb, memberJson);
        move.Parameters.AddWithValue("definitions", NpgsqlDbType.Jsonb, definitionJson);
        move.Parameters.AddWithValue("prior_planner", priorPlanner);
        await move.ExecuteNonQueryAsync(ct);
        // Return the persisted timestamp precision and canonical record. The
        // caller's clock may have finer ticks than PostgreSQL can retain.
        await using var acknowledgment = new NpgsqlCommand("""
            SELECT id,tenant_id,board_id,name,rank,lifecycle_state,created_at,updated_at,version,archived_at,deleted_at,deleted_by
            FROM board_lists WHERE tenant_id=@tenant AND board_id=@destination AND id=@list;
            """, session.Connection, session.Transaction);
        acknowledgment.Parameters.AddWithValue("tenant", organization);
        acknowledgment.Parameters.AddWithValue("destination", destinationBoard); acknowledgment.Parameters.AddWithValue("list", list);
        await using var acknowledged = await acknowledgment.ExecuteReaderAsync(ct);
        if (!await acknowledged.ReadAsync(ct)) throw new InvalidOperationException("List transfer acknowledgment was unavailable.");
        return new(ReadList(acknowledged), Array.AsReadOnly(cards.ToArray()));
    }
}
