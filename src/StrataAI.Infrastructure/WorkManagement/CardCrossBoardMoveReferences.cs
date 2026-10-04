using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    private sealed record MovingMember(Guid UserId, Guid AssignedBy, DateTimeOffset CreatedAt, long Version);
    private sealed record MovingReferences(IReadOnlyList<Guid> Labels, IReadOnlyList<MovingMember> Members);

    private async Task<MovingReferences> DetachMovingReferences(Guid tenant, Guid source, Guid destination,
        Guid card, bool verified, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(tenant))
            throw new InvalidOperationException("Cross-Board references require the owning move transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenant, ct);
        var labels = new List<BoardLabelRecord>(); var members = new List<MovingMember>();
        await using (var query = new NpgsqlCommand($"SELECT {string.Join(',', LabelColumns.Split(',').Select(c => "label." + c))} " +
            "FROM board_labels label JOIN card_labels a ON a.tenant_id=label.tenant_id AND a.board_id=label.board_id AND a.label_id=label.id " +
            "WHERE a.tenant_id=@tenant AND a.board_id=@source AND a.card_id=@card AND label.status='ACTIVE' ORDER BY label.rank,label.id;",
            session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("source", source); query.Parameters.AddWithValue("card", card);
            await using var rows = await query.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct)) labels.Add(ReadLabel(rows));
        }
        await using (var query = new NpgsqlCommand("""
            SELECT a.user_id,a.assigned_by,a.created_at,a.version
            FROM card_members a JOIN board_members m ON m.tenant_id=a.tenant_id AND m.user_id=a.user_id AND m.board_id=@destination
            JOIN organization_members o ON o.tenant_id=a.tenant_id AND o.user_id=a.user_id JOIN users u ON u.id=a.user_id
            WHERE a.tenant_id=@tenant AND a.board_id=@source AND a.card_id=@card
              AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
            ORDER BY a.user_id;
            """, session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("source", source);
            query.Parameters.AddWithValue("destination", destination); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("verified", verified);
            await using var rows = await query.ExecuteReaderAsync(ct);
            while (await rows.ReadAsync(ct)) members.Add(new(rows.GetGuid(0), rows.GetGuid(1), rows.GetFieldValue<DateTimeOffset>(2), rows.GetInt64(3)));
        }
        var copied = new List<Guid>();
        foreach (var label in labels) copied.Add((await CreateLabelAsync(destination, Guid.NewGuid(), label.Name, label.Color, now, ct)).Id);
        await using (var clear = new NpgsqlCommand("DELETE FROM card_labels WHERE tenant_id=@tenant AND card_id=@card; " +
            "DELETE FROM card_members WHERE tenant_id=@tenant AND card_id=@card;", session.Connection, session.Transaction))
        {
            clear.Parameters.AddWithValue("tenant", tenant); clear.Parameters.AddWithValue("card", card);
            await clear.ExecuteNonQueryAsync(ct);
        }
        return new(copied, members);
    }

    private async Task AttachMovingReferences(Guid tenant, Guid destination, Guid card, MovingReferences references,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenant, ct);
        foreach (var label in references.Labels)
        {
            await using var query = new NpgsqlCommand("INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at,version) " +
                "VALUES(@tenant,@board,@card,@label,@now,@now,1);", session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("board", destination);
            query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("label", label); query.Parameters.AddWithValue("now", now);
            await query.ExecuteNonQueryAsync(ct);
        }
        foreach (var member in references.Members)
        {
            await using var query = new NpgsqlCommand("""
                INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by,created_at,updated_at,version)
                VALUES(@tenant,@board,@card,@user,@assigner,@created,@now,@version+1);
                """, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", tenant); query.Parameters.AddWithValue("board", destination);
            query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("user", member.UserId);
            query.Parameters.AddWithValue("assigner", member.AssignedBy); query.Parameters.AddWithValue("created", member.CreatedAt);
            query.Parameters.AddWithValue("now", now); query.Parameters.AddWithValue("version", member.Version);
            await query.ExecuteNonQueryAsync(ct);
        }
    }
}
