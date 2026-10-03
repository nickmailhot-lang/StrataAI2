using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IChecklistStore
{
    private readonly Dictionary<Guid, ChecklistRecord> _checklists = [];
    public Task<ChecklistRecord?> FindAsync(Guid organization, Guid card, Guid checklist, CancellationToken ct, bool includeDeleted = false)
    {
        lock (_sync) return Task.FromResult(_checklists.TryGetValue(checklist, out var value) && value.OrganizationId == organization &&
            value.CardId == card && (includeDeleted || value.DeletedAt is null) ? value : null);
    }
    public Task<ChecklistRecord?> RenameAsync(Guid organization, Guid card, Guid checklist, string title, long version, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_checklists.TryGetValue(checklist, out var value) || value.OrganizationId != organization || value.CardId != card || value.DeletedAt is not null || value.Version != version)
                return Task.FromResult<ChecklistRecord?>(null);
            var updated = value with { Title = title, Version = value.Version + 1, UpdatedAt = now };
            _checklists[checklist] = updated; return Task.FromResult<ChecklistRecord?>(updated);
        }
    }
    public Task<IReadOnlyList<ChecklistSummary>> ListAsync(Guid organization, Guid card, string? afterRank, Guid? afterId, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<ChecklistSummary>>(_checklists.Values
            .Where(value => value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null &&
                (afterRank is null || string.CompareOrdinal(value.Rank, afterRank) > 0 || value.Rank == afterRank && value.Id.CompareTo(afterId!.Value) > 0))
            .OrderBy(value => value.Rank, StringComparer.Ordinal).ThenBy(value => value.Id).Take(51).Select(value => Summary(value)).ToArray());
    }
    public Task<string> NextRankAsync(Guid organization, Guid card, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult(RankToken.After(_checklists.Values
            .Where(value => value.OrganizationId == organization && value.CardId == card && value.DeletedAt is null)
            .OrderByDescending(value => value.Rank, StringComparer.Ordinal).FirstOrDefault()?.Rank));
    }
    public Task<ChecklistRecord> CreateAsync(Guid organization, Guid card, string title, string rank, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(card, out var parent) || parent.OrganizationId != organization) throw new InvalidOperationException("Checklist parent unavailable.");
            var value = new ChecklistRecord(Guid.NewGuid(), organization, card, title, rank, now, now, 1, null);
            _checklists.Add(value.Id, value); return Task.FromResult(value);
        }
    }
}

internal sealed partial class PostgresWorkManagementStore : IChecklistStore
{
    private const string ChecklistColumns = "c.id,c.tenant_id,c.card_id,c.title,c.rank,c.created_at,c.updated_at,c.version,c.deleted_at";
    private static ChecklistRecord ReadChecklist(NpgsqlDataReader row) => new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetString(3), row.GetString(4),
        row.GetFieldValue<DateTimeOffset>(5), row.GetFieldValue<DateTimeOffset>(6), row.GetInt64(7), row.IsDBNull(8) ? null : row.GetFieldValue<DateTimeOffset>(8));
    public async Task<ChecklistRecord?> FindAsync(Guid organization, Guid card, Guid checklist, CancellationToken ct, bool includeDeleted = false)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist reads require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"SELECT {ChecklistColumns} FROM checklists c WHERE c.tenant_id=@tenant AND c.card_id=@card AND c.id=@id AND (@deleted OR c.deleted_at IS NULL);", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card); query.Parameters.AddWithValue("id", checklist);
        query.Parameters.AddWithValue("deleted", includeDeleted);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadChecklist(reader) : null;
    }
    public async Task<ChecklistRecord?> RenameAsync(Guid organization, Guid card, Guid checklist, string title, long version, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist edits require the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var update = new NpgsqlCommand($"UPDATE checklists AS c SET title=@title,version=c.version+1,updated_at=@now WHERE c.tenant_id=@tenant AND c.card_id=@card AND c.id=@id AND c.version=@version AND c.deleted_at IS NULL RETURNING {ChecklistColumns};", session.Connection, session.Transaction);
        update.Parameters.AddWithValue("tenant", organization); update.Parameters.AddWithValue("card", card); update.Parameters.AddWithValue("id", checklist);
        update.Parameters.AddWithValue("title", title); update.Parameters.AddWithValue("version", version); update.Parameters.AddWithValue("now", now);
        await using var reader = await update.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadChecklist(reader) : null;
    }
    public async Task<IReadOnlyList<ChecklistSummary>> ListAsync(Guid organization, Guid card, string? afterRank, Guid? afterId, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist reads require the owning scope.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand($"""
            SELECT {ChecklistColumns},p.completed,p.total FROM checklists c
            CROSS JOIN LATERAL (SELECT count(*) FILTER(WHERE i.completed) AS completed,count(*) AS total
              FROM checklist_items i WHERE i.tenant_id=c.tenant_id AND i.checklist_id=c.id AND i.deleted_at IS NULL) p
            WHERE c.tenant_id=@tenant AND c.card_id=@card AND c.deleted_at IS NULL
              AND (@rank IS NULL OR c.rank>@rank OR (c.rank=@rank AND c.id>@after))
            ORDER BY c.rank,c.id LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("card", card);
        query.Parameters.AddWithValue("rank", NpgsqlDbType.Text, (object?)afterRank ?? DBNull.Value);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)afterId ?? DBNull.Value);
        var rows = new List<ChecklistSummary>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(new(ReadChecklist(reader), reader.GetInt64(9), reader.GetInt64(10)));
        return rows;
    }
    public async Task<string> NextRankAsync(Guid organization, Guid card, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist rank reads require the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var tail = new NpgsqlCommand("SELECT rank FROM checklists WHERE tenant_id=@tenant AND card_id=@card AND deleted_at IS NULL ORDER BY rank DESC LIMIT 1;", session.Connection, session.Transaction);
        tail.Parameters.AddWithValue("tenant", organization); tail.Parameters.AddWithValue("card", card);
        return RankToken.After(await tail.ExecuteScalarAsync(ct) as string);
    }
    public async Task<ChecklistRecord> CreateAsync(Guid organization, Guid card, string title, string rank, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist creation requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        var value = new ChecklistRecord(Guid.NewGuid(), organization, card, title, rank, now, now, 1, null);
        await using var insert = new NpgsqlCommand($"INSERT INTO checklists AS c(id,tenant_id,card_id,title,rank,created_at,updated_at) VALUES(@id,@tenant,@card,@title,@rank,@now,@now) RETURNING {ChecklistColumns};", session.Connection, session.Transaction);
        insert.Parameters.AddWithValue("id", value.Id); insert.Parameters.AddWithValue("tenant", organization); insert.Parameters.AddWithValue("card", card);
        insert.Parameters.AddWithValue("title", title); insert.Parameters.AddWithValue("rank", value.Rank); insert.Parameters.AddWithValue("now", now);
        await using var reader = await insert.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Checklist creation returned no record.");
        return ReadChecklist(reader);
    }
}
