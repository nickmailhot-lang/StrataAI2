using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<ChecklistDeletionStoreResult?> DeleteAsync(Guid organization, Guid card, Guid checklist, long version, Guid actor, string correlationId, DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_checklists.TryGetValue(checklist, out var value) || value.OrganizationId != organization || value.CardId != card || value.DeletedAt is not null || value.Version != version)
                return Task.FromResult<ChecklistDeletionStoreResult?>(null);
            var active = _checklistItems.Values.Where(item => item.OrganizationId == organization && item.ChecklistId == checklist && item.DeletedAt is null).ToArray();
            foreach (var item in active) _checklistItems[item.Id] = item with { DeletedAt = now, UpdatedAt = now, Version = item.Version + 1 };
            var updated = value with { DeletedAt = now, UpdatedAt = now, Version = version + 1 };
            _checklists[checklist] = updated; return Task.FromResult<ChecklistDeletionStoreResult?>(new(updated, active.LongLength));
        }
    }
}
internal sealed partial class PostgresWorkManagementStore
{
    public async Task<ChecklistDeletionStoreResult?> DeleteAsync(Guid organization, Guid card, Guid checklist, long version, Guid actor, string correlationId, DateTimeOffset now, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(organization)) throw new InvalidOperationException("Checklist deletion requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organization, ct);
        // Cascade and per-item audit stay in PostgreSQL; no unbounded child result
        // is materialized or disclosed, and previous tombstones remain unchanged.
        await using var command = new NpgsqlCommand($$"""
            WITH parent AS (
              UPDATE checklists AS c SET deleted_at=@now,updated_at=@now,version=c.version+1
              WHERE c.tenant_id=@tenant AND c.card_id=@card AND c.id=@id AND c.version=@version AND c.deleted_at IS NULL RETURNING c.*
            ), deleted_items AS (
              UPDATE checklist_items AS i SET deleted_at=@now,updated_at=@now,version=i.version+1
              WHERE i.tenant_id=@tenant AND i.checklist_id=@id AND i.deleted_at IS NULL AND EXISTS(SELECT 1 FROM parent) RETURNING i.id
            ), audited AS (
              INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata,created_at)
              SELECT gen_random_uuid(),@tenant,@actor,'CHECKLIST_ITEM_DELETED','ChecklistItem',id,@correlation,'{}'::jsonb,@now FROM deleted_items RETURNING id
            ) SELECT {{ChecklistColumns}},(SELECT count(*) FROM audited) FROM parent c;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("card", card); command.Parameters.AddWithValue("id", checklist);
        command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("correlation", correlationId); command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(ReadChecklist(reader), reader.GetInt64(9)) : null;
    }
}
