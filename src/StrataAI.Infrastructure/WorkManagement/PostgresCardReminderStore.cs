using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresCardReminderStore(PostgresConnectionFactory connections) : ICardReminderStore
{
    private const string Columns = "id,tenant_id,user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version";
    public async Task<CardReminder?> FindAsync(Guid organizationId, Guid userId, Guid cardId, CancellationToken ct)
    {
        await using var session = await connections.OpenTenantSessionAsync(organizationId, ct);
        await using var query = new NpgsqlCommand($"SELECT {Columns} FROM card_reminders WHERE tenant_id=@tenant AND user_id=@user AND card_id=@card;", session.Connection, session.Transaction);
        Bind(query, organizationId, userId, cardId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<IReadOnlyList<CardReminder>> ListEnabledForCardAsync(Guid organizationId, Guid cardId, CancellationToken ct)
    {
        if (!connections.HasCommandScope(organizationId)) throw new InvalidOperationException("Reminder planning requires the owning Card transaction.");
        await using var session = await connections.OpenTenantSessionAsync(organizationId, ct);
        await using var query = new NpgsqlCommand($"SELECT {Columns} FROM card_reminders WHERE tenant_id=@tenant AND card_id=@card AND enabled ORDER BY user_id;", session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("card", cardId);
        var rows = new List<CardReminder>(); await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(Read(reader));
        return rows;
    }
    public async Task<CardReminder?> SetAsync(CardRecord card, Guid userId, string intervalCode, bool enabled,
        long expectedVersion, DateTimeOffset now, CancellationToken ct, bool contextActive = true)
    {
        if (!connections.HasCommandScope(card.OrganizationId)) throw new InvalidOperationException("Reminder changes require the owning Card transaction.");
        var plan = CardReminderPlan.For(card, intervalCode, enabled, now, contextActive);
        var current = await FindAsync(card.OrganizationId, userId, card.Id, ct);
        if ((current?.Version ?? 0) != expectedVersion) return null;
        if (current is not null && current.IntervalCode == intervalCode && plan.Matches(current)) return current;
        await using var session = await connections.OpenTenantSessionAsync(card.OrganizationId, ct);
        await using var query = new NpgsqlCommand(expectedVersion == 0 ? $"""
            INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version)
            VALUES(@tenant,@id,@user,@card,@interval,@enabled,@due,@trigger,@status,1,@now,@now,1)
            ON CONFLICT(tenant_id,user_id,card_id) DO NOTHING RETURNING {Columns};
            """ : $"""
            UPDATE card_reminders SET interval_code=@interval,enabled=@enabled,due_at=@due,trigger_at=@trigger,status=@status,
                generation=generation+1,version=version+1,updated_at=GREATEST(updated_at,@now)
            WHERE tenant_id=@tenant AND user_id=@user AND card_id=@card AND version=@version RETURNING {Columns};
            """, session.Connection, session.Transaction);
        Bind(query, card.OrganizationId, userId, card.Id);
        query.Parameters.AddWithValue("interval", intervalCode); query.Parameters.AddWithValue("enabled", plan.Enabled);
        query.Parameters.AddWithValue("due", NpgsqlDbType.TimestampTz, (object?)plan.DueAt ?? DBNull.Value);
        query.Parameters.AddWithValue("trigger", NpgsqlDbType.TimestampTz, (object?)plan.TriggerAt ?? DBNull.Value);
        query.Parameters.AddWithValue("status", plan.Status); query.Parameters.AddWithValue("now", now);
        if (expectedVersion == 0) query.Parameters.AddWithValue("id", Guid.NewGuid()); else query.Parameters.AddWithValue("version", expectedVersion);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    private static void Bind(NpgsqlCommand query, Guid organizationId, Guid userId, Guid cardId)
    {
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("card", cardId);
    }
    private static CardReminder Read(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
        reader.GetString(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
        reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.GetString(8), reader.GetInt64(9),
        reader.GetFieldValue<DateTimeOffset>(10), reader.GetFieldValue<DateTimeOffset>(11), reader.GetInt64(12));
}
