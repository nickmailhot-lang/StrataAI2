using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<ChecklistItemRecord?> DeleteItemAsync(Guid organization, Guid checklist, Guid item, long version, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_checklistItems.TryGetValue(item, out var value) || value.OrganizationId != organization || value.ChecklistId != checklist || value.DeletedAt is not null || value.Version != version)
                return Task.FromResult<ChecklistItemRecord?>(null);
            var updated = value with { DeletedAt = now, UpdatedAt = now, Version = version + 1 };
            _checklistItems[item] = updated; return Task.FromResult<ChecklistItemRecord?>(updated);
        }
    }
}
internal sealed partial class PostgresWorkManagementStore
{
    public async Task<ChecklistItemRecord?> DeleteItemAsync(Guid organization, Guid checklist, Guid item, long version, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist item deletion requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        await using var update = new NpgsqlCommand($"UPDATE checklist_items AS i SET deleted_at=@now,updated_at=@now,version=i.version+1 WHERE i.tenant_id=@tenant AND i.checklist_id=@checklist AND i.id=@id AND i.version=@version AND i.deleted_at IS NULL RETURNING {ChecklistItemColumns};", session.Connection, session.Transaction);
        update.Parameters.AddWithValue("tenant", organization); update.Parameters.AddWithValue("checklist", checklist); update.Parameters.AddWithValue("id", item);
        update.Parameters.AddWithValue("version", version); update.Parameters.AddWithValue("now", now);
        await using var reader = await update.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadChecklistItem(reader) : null;
    }
}
