using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore : IListBoardMovementPublicationStore
{
    public async Task<IReadOnlyDictionary<Guid, CardRecord>> ReadCardsAsync(BoardListRecord destination,
        IReadOnlyList<Guid> cardIds, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(destination.OrganizationId))
            throw new InvalidOperationException("List publication requires its owning command transaction.");
        var ids = PublicationIds(cardIds);
        await using var session = await connectionFactory.OpenTenantSessionAsync(destination.OrganizationId, ct);
        await using var query = new NpgsqlCommand("""
            SELECT id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,version,
                start_at,due_at,due_timezone,due_has_time,due_complete,archived_at,deleted_at,deleted_by
            FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND id=ANY(@ids);
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", destination.OrganizationId); query.Parameters.AddWithValue("board", destination.BoardId);
        query.Parameters.AddWithValue("list", destination.Id); query.Parameters.AddWithValue("ids", ids);
        var rows = new Dictionary<Guid, CardRecord>();
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { var card = ReadCard(reader); rows.Add(card.Id, card); }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, CardRecord>(rows);
    }

    public async Task AppendCardAuditsAsync(BoardListRecord destination, Guid actor, IReadOnlyList<Guid> cardIds,
        string correlationId, CancellationToken ct)
    {
        if (!connectionFactory.HasCommandScope(destination.OrganizationId))
            throw new InvalidOperationException("List publication requires its owning command transaction.");
        var ids = PublicationIds(cardIds);
        if (actor == Guid.Empty) throw new ArgumentException("Audit actor is required.", nameof(actor));
        await using var session = await connectionFactory.OpenTenantSessionAsync(destination.OrganizationId, ct);
        await using var query = new NpgsqlCommand("""
            INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata,created_at)
            SELECT gen_random_uuid(),tenant_id,@actor,'CARD_MOVED','Card',id,@correlation,'{}'::jsonb,now()
            FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND id=ANY(@ids);
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", destination.OrganizationId); query.Parameters.AddWithValue("board", destination.BoardId);
        query.Parameters.AddWithValue("list", destination.Id); query.Parameters.AddWithValue("ids", ids);
        query.Parameters.AddWithValue("actor", actor); query.Parameters.AddWithValue("correlation", correlationId);
        if (await query.ExecuteNonQueryAsync(ct) != ids.Length)
            throw new InvalidOperationException("List publication audit omitted a Card reference.");
    }

    private static Guid[] PublicationIds(IReadOnlyList<Guid> ids)
    {
        var snapshot = ids.ToArray();
        if (snapshot.Any(id => id == Guid.Empty) || snapshot.Distinct().Count() != snapshot.Length)
            throw new ArgumentException("List publication requires distinct nonempty Card references.", nameof(ids));
        return snapshot;
    }
}
