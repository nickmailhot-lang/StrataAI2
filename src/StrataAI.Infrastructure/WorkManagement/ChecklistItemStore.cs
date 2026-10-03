using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<Guid, ChecklistItemRecord> _checklistItems = [];
    private ChecklistSummary Summary(ChecklistRecord checklist)
    {
        var items = _checklistItems.Values.Where(value => value.OrganizationId == checklist.OrganizationId && value.ChecklistId == checklist.Id && value.DeletedAt is null).ToArray();
        return new(checklist, items.LongCount(value => value.Completed), items.LongLength);
    }
    public Task<ChecklistSummary?> GetSummaryAsync(Guid organization, Guid card, Guid checklist, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult(_checklists.TryGetValue(checklist, out var value) && value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null ? Summary(value) : null);
    }
    public Task<IReadOnlyList<ChecklistItemRecord>> ListItemsAsync(Guid organization, Guid checklist, string? afterRank, Guid? afterId, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<ChecklistItemRecord>>(_checklistItems.Values
            .Where(value => value.OrganizationId == organization && value.ChecklistId == checklist && value.DeletedAt is null &&
                (afterRank is null || string.CompareOrdinal(value.Rank, afterRank) > 0 || value.Rank == afterRank && value.Id.CompareTo(afterId!.Value) > 0))
            .OrderBy(value => value.Rank, StringComparer.Ordinal).ThenBy(value => value.Id).Take(51).ToArray());
    }
    public Task<string> NextItemRankAsync(Guid organization, Guid checklist, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult(RankToken.After(_checklistItems.Values
            .Where(value => value.OrganizationId == organization && value.ChecklistId == checklist && value.DeletedAt is null)
            .OrderByDescending(value => value.Rank, StringComparer.Ordinal).FirstOrDefault()?.Rank));
    }
    public Task<ChecklistItemRecord> CreateItemAsync(Guid organization, Guid checklist, string text, string rank, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_checklists.TryGetValue(checklist, out var parent) || parent.OrganizationId != organization || parent.DeletedAt is not null) throw new InvalidOperationException("Checklist parent unavailable.");
            var value = new ChecklistItemRecord(Guid.NewGuid(), organization, checklist, text, rank, false, null, null, now, now, 1, null);
            _checklistItems.Add(value.Id, value); return Task.FromResult(value);
        }
    }
}

internal sealed partial class PostgresWorkManagementStore
{
    private const string ChecklistItemColumns = "i.id,i.tenant_id,i.checklist_id,i.text,i.rank,i.completed,i.completed_at,i.completed_by,i.created_at,i.updated_at,i.version,i.deleted_at";
    private static ChecklistItemRecord ReadChecklistItem(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetString(3), row.GetString(4),
        row.GetBoolean(5), row.IsDBNull(6) ? null : row.GetFieldValue<DateTimeOffset>(6), row.IsDBNull(7) ? null : row.GetGuid(7),
        row.GetFieldValue<DateTimeOffset>(8), row.GetFieldValue<DateTimeOffset>(9), row.GetInt64(10), row.IsDBNull(11) ? null : row.GetFieldValue<DateTimeOffset>(11));
    public async Task<ChecklistSummary?> GetSummaryAsync(Guid organization, Guid card, Guid checklist, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist summaries require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            SELECT {ChecklistColumns},p.completed,p.total FROM checklists c
            CROSS JOIN LATERAL (SELECT count(*) FILTER(WHERE i.completed) AS completed,count(*) AS total
              FROM checklist_items i WHERE i.tenant_id=c.tenant_id AND i.checklist_id=c.id AND i.deleted_at IS NULL) p
            WHERE c.tenant_id=@tenant AND c.card_id=@card AND c.id=@id AND c.deleted_at IS NULL;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("id", checklist);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(ReadChecklist(reader), reader.GetInt64(9), reader.GetInt64(10)) : null;
    }
    public async Task<IReadOnlyList<ChecklistItemRecord>> ListItemsAsync(Guid organization, Guid checklist, string? afterRank, Guid? afterId, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist item reads require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {ChecklistItemColumns} FROM checklist_items i WHERE i.tenant_id=@tenant AND i.checklist_id=@checklist AND i.deleted_at IS NULL AND (@rank IS NULL OR i.rank>@rank OR (i.rank=@rank AND i.id>@after)) ORDER BY i.rank,i.id LIMIT 51;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("checklist", checklist);
        query.Parameters.AddWithValue("rank", NpgsqlDbType.Text, (object?)afterRank ?? DBNull.Value); query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)afterId ?? DBNull.Value);
        var rows = new List<ChecklistItemRecord>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadChecklistItem(reader)); return rows;
    }
    public async Task<string> NextItemRankAsync(Guid organization, Guid checklist, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist item ranks require the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("SELECT rank FROM checklist_items WHERE tenant_id=@tenant AND checklist_id=@checklist AND deleted_at IS NULL ORDER BY rank DESC LIMIT 1;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("checklist", checklist);
        return RankToken.After(await query.ExecuteScalarAsync(ct) as string);
    }
    public async Task<ChecklistItemRecord> CreateItemAsync(Guid organization, Guid checklist, string text, string rank, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist item creation requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var insert = new NpgsqlCommand($"INSERT INTO checklist_items AS i(id,tenant_id,checklist_id,text,rank,created_at,updated_at) VALUES(@id,@tenant,@checklist,@text,@rank,@now,@now) RETURNING {ChecklistItemColumns};", session.Connection, session.Transaction);
        insert.Parameters.AddWithValue("id", Guid.NewGuid()); insert.Parameters.AddWithValue("tenant", organization); insert.Parameters.AddWithValue("checklist", checklist);
        insert.Parameters.AddWithValue("text", text); insert.Parameters.AddWithValue("rank", rank); insert.Parameters.AddWithValue("now", now);
        await using var reader = await insert.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Checklist item creation returned no record.");
        return ReadChecklistItem(reader);
    }
}
