using Npgsql;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCardMassMentionQuota(PostgresConnectionFactory connections) : ICardMassMentionQuota
{
    public async Task<bool> TryReserveAsync(WorkEvent source, CancellationToken cancellationToken = default)
    {
        CardMassMentionQuotaValidation.Check(source);
        if (!connections.HasCommandScope(source.OrganizationId)) throw new InvalidOperationException("Mass mention quota requires the owning Work transaction.");
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = await connections.OpenTenantSessionAsync(source.OrganizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", source.OrganizationId); command.Parameters.AddWithValue("board", source.BoardId);
            command.Parameters.AddWithValue("actor", source.ActorId); command.Parameters.AddWithValue("event", source.EventId);
            return command;
        }
        await using (var gate = Query("SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board FOR UPDATE;"))
            if (await gate.ExecuteScalarAsync(cancellationToken) is null) throw new InvalidOperationException("Mass mention Board is unavailable.");
        await using (var existing = Query("SELECT board_id,actor_id,card_id,card_version,source_created_at FROM mass_mention_reservations WHERE tenant_id=@tenant AND event_id=@event;"))
        await using (var row = await existing.ExecuteReaderAsync(cancellationToken))
            if (await row.ReadAsync(cancellationToken))
            {
                if (row.GetGuid(0) != source.BoardId || row.GetGuid(1) != source.ActorId || row.GetGuid(2) != source.EntityId
                    || row.GetInt64(3) != source.Version || row.GetFieldValue<DateTimeOffset>(4) != source.CreatedAt)
                    throw new InvalidOperationException("Mass mention reservation identity was reused.");
                return true;
            }
        await using (var count = Query("SELECT count(*) FROM mass_mention_reservations WHERE tenant_id=@tenant AND board_id=@board AND actor_id=@actor AND reserved_at>clock_timestamp()-interval '10 minutes';"))
            if ((long)(await count.ExecuteScalarAsync(cancellationToken))! >= ICardMassMentionQuota.MaximumReservations) return false;
        await using var insert = Query("""
            INSERT INTO mass_mention_reservations(tenant_id,event_id,board_id,actor_id,card_id,card_version,source_created_at,reserved_at)
            VALUES(@tenant,@event,@board,@actor,@card,@version,@at,clock_timestamp());
            """);
        insert.Parameters.AddWithValue("card", source.EntityId); insert.Parameters.AddWithValue("version", source.Version);
        insert.Parameters.AddWithValue("at", source.CreatedAt); await insert.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
internal sealed class InMemoryCardMassMentionQuota(DemoWorkTransactionScope scope, IClock clock, InMemoryWorkEventStore events)
    : ICardMassMentionQuota, IDemoWorkTransactionParticipant
{
    private readonly Dictionary<(Guid Tenant, Guid Event), (WorkEvent Source, DateTimeOffset ReservedAt)> _rows = [];
    public Action CaptureRollback()
    {
        lock (_rows) { var restore = DemoRollback.Dictionary(_rows); return () => { lock (_rows) restore(); }; }
    }
    public Task<bool> TryReserveAsync(WorkEvent source, CancellationToken cancellationToken = default)
    {
        CardMassMentionQuotaValidation.Check(source);
        if (!scope.Owns(source.OrganizationId)) throw new InvalidOperationException("Mass mention quota requires the owning Work transaction.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!events.ContainsExact(source)) throw new InvalidOperationException("Mass mention source is unavailable.");
        lock (_rows)
        {
            var key = (source.OrganizationId, source.EventId);
            if (_rows.TryGetValue(key, out var existing))
            {
                if (existing.Source != source) throw new InvalidOperationException("Mass mention reservation identity was reused.");
                return Task.FromResult(true);
            }
            var now = clock.UtcNow;
            if (_rows.Values.Count(row => row.Source.OrganizationId == source.OrganizationId && row.Source.BoardId == source.BoardId
                && row.Source.ActorId == source.ActorId && row.ReservedAt > now.AddMinutes(-10)) >= ICardMassMentionQuota.MaximumReservations)
                return Task.FromResult(false);
            _rows.Add(key, (source, now)); return Task.FromResult(true);
        }
    }
}
internal static class CardMassMentionQuotaValidation
{
    internal static void Check(WorkEvent source)
    {
        ArgumentNullException.ThrowIfNull(source); _ = CardNotification.FromMention(source, source.ActorId);
        if (source.CreatedAt.Offset != TimeSpan.Zero || source.CreatedAt.UtcTicks % 10 != 0)
            throw new ArgumentException("Mass mention source timestamp is invalid.");
    }
}
