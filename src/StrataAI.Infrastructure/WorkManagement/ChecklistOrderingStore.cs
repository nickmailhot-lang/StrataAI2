using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<string> PositionRankAsync(Guid organization, Guid parent, Guid moving, string currentRank, Guid? before, bool item, CancellationToken ct)
    {
        lock (_sync)
        {
            var siblings = item ? _checklistItems.Values.Where(value => value.OrganizationId == organization && value.ChecklistId == parent && value.Id != moving && value.DeletedAt is null).Select(value => (value.Id, value.Rank))
                : _checklists.Values.Where(value => value.OrganizationId == organization && value.CardId == parent && value.Id != moving && value.DeletedAt is null).Select(value => (value.Id, value.Rank));
            var rows = siblings.OrderBy(value => value.Rank, StringComparer.Ordinal).ToArray();
            var position = before is null ? rows.Length : Array.FindIndex(rows, value => value.Id == before);
            if (position < 0) throw new ArgumentException("Invalid checklist position.");
            var upper = position == rows.Length ? null : rows[position].Rank;
            var lower = position == 0 ? null : rows[position - 1].Rank;
            return Task.FromResult(ChecklistOrdering.Rank(currentRank, lower, upper));
        }
    }
    public Task<ChecklistRecord?> UpdateRankAsync(Guid organization, Guid card, Guid checklist, string rank, long version, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_checklists.TryGetValue(checklist, out var value) || value.OrganizationId != organization || value.CardId != card || value.DeletedAt is not null || value.Version != version) return Task.FromResult<ChecklistRecord?>(null);
            var updated = value with { Rank = rank, Version = version + 1, UpdatedAt = now };
            _checklists[checklist] = updated; return Task.FromResult<ChecklistRecord?>(updated);
        }
    }
    public Task<ChecklistItemRecord?> UpdateItemRankAsync(Guid organization, Guid checklist, Guid item, string rank, long version, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_checklistItems.TryGetValue(item, out var value) || value.OrganizationId != organization || value.ChecklistId != checklist || value.DeletedAt is not null || value.Version != version) return Task.FromResult<ChecklistItemRecord?>(null);
            var updated = value with { Rank = rank, Version = version + 1, UpdatedAt = now };
            _checklistItems[item] = updated; return Task.FromResult<ChecklistItemRecord?>(updated);
        }
    }
}
internal sealed partial class PostgresWorkManagementStore
{
    public async Task<string> PositionRankAsync(Guid organization, Guid parent, Guid moving, string currentRank, Guid? before, bool item, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist ordering requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        // Identifiers are selected solely from this internal two-table contract.
        var table = item ? "checklist_items" : "checklists"; var parentColumn = item ? "checklist_id" : "card_id";
        string? upper = null;
        if (before is not null)
        {
            await using var anchor = new NpgsqlCommand($"SELECT rank FROM {table} WHERE tenant_id=@tenant AND {parentColumn}=@parent AND id=@before AND id<>@moving AND deleted_at IS NULL;", session.Connection, session.Transaction);
            anchor.Parameters.AddWithValue("tenant", organization); anchor.Parameters.AddWithValue("parent", parent);
            anchor.Parameters.AddWithValue("before", before.Value); anchor.Parameters.AddWithValue("moving", moving);
            upper = await anchor.ExecuteScalarAsync(ct) as string;
            if (upper is null) throw new ArgumentException("Invalid checklist position.");
        }
        await using var previous = new NpgsqlCommand($"SELECT rank FROM {table} WHERE tenant_id=@tenant AND {parentColumn}=@parent AND id<>@moving AND deleted_at IS NULL AND (@upper IS NULL OR rank<@upper) ORDER BY rank DESC LIMIT 1;", session.Connection, session.Transaction);
        previous.Parameters.AddWithValue("tenant", organization); previous.Parameters.AddWithValue("parent", parent); previous.Parameters.AddWithValue("moving", moving);
        previous.Parameters.AddWithValue("upper", NpgsqlDbType.Text, (object?)upper ?? DBNull.Value);
        return ChecklistOrdering.Rank(currentRank, await previous.ExecuteScalarAsync(ct) as string, upper);
    }
    public async Task<ChecklistRecord?> UpdateRankAsync(Guid organization, Guid card, Guid checklist, string rank, long version, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist ordering requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var update = new NpgsqlCommand($"UPDATE checklists AS c SET rank=@rank,version=c.version+1,updated_at=@now WHERE c.tenant_id=@tenant AND c.card_id=@parent AND c.id=@id AND c.version=@version AND c.deleted_at IS NULL RETURNING {ChecklistColumns};", session.Connection, session.Transaction);
        update.Parameters.AddWithValue("tenant", organization); update.Parameters.AddWithValue("parent", card); update.Parameters.AddWithValue("id", checklist);
        update.Parameters.AddWithValue("rank", rank); update.Parameters.AddWithValue("version", version); update.Parameters.AddWithValue("now", now);
        await using var reader = await update.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadChecklist(reader) : null;
    }
    public async Task<ChecklistItemRecord?> UpdateItemRankAsync(Guid organization, Guid checklist, Guid item, string rank, long version, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist item ordering requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var update = new NpgsqlCommand($"UPDATE checklist_items AS i SET rank=@rank,version=i.version+1,updated_at=@now WHERE i.tenant_id=@tenant AND i.checklist_id=@parent AND i.id=@id AND i.version=@version AND i.deleted_at IS NULL RETURNING {ChecklistItemColumns};", session.Connection, session.Transaction);
        update.Parameters.AddWithValue("tenant", organization); update.Parameters.AddWithValue("parent", checklist); update.Parameters.AddWithValue("id", item);
        update.Parameters.AddWithValue("rank", rank); update.Parameters.AddWithValue("version", version); update.Parameters.AddWithValue("now", now);
        await using var reader = await update.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadChecklistItem(reader) : null;
    }
}
internal static class ChecklistOrdering
{
    internal static string Rank(string current, string? lower, string? upper) =>
        (lower is null || string.CompareOrdinal(current, lower) > 0) && (upper is null || string.CompareOrdinal(current, upper) < 0)
            ? current : upper is null ? RankToken.After(lower) : RankToken.Between(lower, upper);
}
