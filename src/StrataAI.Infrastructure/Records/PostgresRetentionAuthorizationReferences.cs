using Npgsql;
using StrataAI.Application.Records;
using StrataAI.Domain.Records;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.Records;

// Unregistered until actual source admission and Demo parity exist. No new grants.
internal sealed class PostgresRetentionAuthorizationReferences(PostgresConnectionFactory connections)
    : IRetentionAuthorizationReferences, IRetentionWorkSourceReferences
{
    private const string HoldFields = "h.tenant_id,h.hold_id,h.version,h.record_class,(h.category IS NOT NULL),h.record_id,h.authorization_context_version,h.classification_board_id,h.application_board_id,h.application_source_version ";

    public async Task<RetentionSourceReference?> ReadSourceAsync(Guid organization, RetentionRecordClass recordClass,
        Guid record, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (record == Guid.Empty || !Enum.IsDefined(recordClass)) return null;
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT tenant_id,record_class,record_id,source_kind,parent_card_id,
                   authorization_context_version,original_board_id,source_version
            FROM record_retention_sources WHERE tenant_id=@tenant AND record_class=@class AND record_id=@record;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        command.Parameters.AddWithValue("class", recordClass == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : recordClass.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("record", record);
        await using var row = await command.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        var kind = row.GetString(3) switch
        {
            "CARD" => RetentionProducerKind.Card, "CARD_COMMENT" => RetentionProducerKind.CardComment,
            "ATTACHMENT" => RetentionProducerKind.Attachment, "AUDIT_EVENT" => RetentionProducerKind.AuditEvent,
            _ => throw new InvalidOperationException("Unknown retention producer.")
        };
        return new(row.GetGuid(0), Class(row, 1)!.Value, row.GetGuid(2), kind, GuidValue(row, 4),
            row.IsDBNull(5) ? null : row.GetInt16(5), GuidValue(row, 6), row.IsDBNull(7) ? null : row.GetInt64(7));
    }

    public Task<RetentionHoldReference?> ReadHoldAsync(Guid organization, Guid hold, CancellationToken ct) =>
        ReadAsync(organization, hold, null, null, ct);
    public Task<RetentionHoldReference?> ReadReceiptAsync(Guid organization, Guid actor, Guid key, CancellationToken ct) =>
        ReadAsync(organization, null, actor, key, ct);

    public async Task<RetentionWorkSourceReference?> ReadAsync(Guid organization, RetentionRecordClass recordClass,
        Guid record, CancellationToken ct)
    {
        RequireScope(organization, ct);
        var classification = await ReadSourceAsync(organization, recordClass, record, ct);
        if (classification is null || classification.Kind == RetentionProducerKind.AuditEvent) return null;
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,c.board_id,c.list_id,c.version,l.version,
              CASE r.source_kind WHEN 'CARD' THEN c.version WHEN 'CARD_COMMENT' THEN m.version ELSE a.version END,
              c.lifecycle_state,l.lifecycle_state,
              CASE r.source_kind WHEN 'CARD' THEN c.lifecycle_state
                WHEN 'CARD_COMMENT' THEN CASE WHEN m.deleted_at IS NULL THEN 'ACTIVE' ELSE 'DELETED' END
                ELSE a.lifecycle_state END
            FROM record_retention_sources r
            JOIN cards c ON c.tenant_id=r.tenant_id
              AND c.id=CASE WHEN r.source_kind='CARD' THEN r.record_id ELSE r.parent_card_id END
            JOIN board_lists l ON(l.tenant_id,l.id,l.board_id)=(c.tenant_id,c.list_id,c.board_id)
            LEFT JOIN card_comments m ON r.source_kind='CARD_COMMENT'
              AND(m.tenant_id,m.id,m.card_id)=(r.tenant_id,r.record_id,r.parent_card_id)
            LEFT JOIN attachments a ON r.source_kind='ATTACHMENT'
              AND(a.tenant_id,a.id,a.card_id)=(r.tenant_id,r.record_id,r.parent_card_id)
            WHERE r.tenant_id=@tenant AND r.record_class=@class AND r.record_id=@record
              AND(r.source_kind='CARD' OR r.source_kind='CARD_COMMENT' AND m.id IS NOT NULL
                OR r.source_kind='ATTACHMENT' AND a.id IS NOT NULL);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        command.Parameters.AddWithValue("class", recordClass == RetentionRecordClass.OperationalWork ? "OPERATIONAL_WORK" : recordClass.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("record", record);
        await using var row = await command.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(classification, row.GetGuid(0), row.GetGuid(1), row.GetGuid(2),
            row.GetInt64(3), row.GetInt64(4), row.GetInt64(5), State(row, 6), State(row, 7), State(row, 8)) : null;
    }
    private static WorkItemLifecycleState State(NpgsqlDataReader row, int index) => Enum.Parse<WorkItemLifecycleState>(row.GetString(index), true);

    private async Task<RetentionHoldReference?> ReadAsync(Guid organization, Guid? hold, Guid? actor, Guid? key, CancellationToken ct)
    {
        RequireScope(organization, ct);
        if (hold == Guid.Empty || actor == Guid.Empty || key == Guid.Empty) return null;
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        var query = hold.HasValue
            ? "SELECT " + HoldFields + """
               FROM record_retention_holds p JOIN record_retention_hold_history h
                ON(h.tenant_id,h.hold_id,h.version)=(p.tenant_id,p.id,p.version)
               WHERE p.tenant_id=@tenant AND p.id=@hold;
               """
            : "SELECT " + HoldFields + """
               FROM record_retention_hold_receipts r JOIN record_retention_hold_history h
                ON(h.tenant_id,h.hold_id,h.version,h.actor_id,h.key_id)=(r.tenant_id,r.hold_id,r.version,r.actor_id,r.key_id)
               WHERE r.tenant_id=@tenant AND r.actor_id=@actor AND r.key_id=@key;
               """;
        await using var command = new NpgsqlCommand(query, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization);
        if (hold.HasValue) command.Parameters.AddWithValue("hold", hold.Value);
        else { command.Parameters.AddWithValue("actor", actor!.Value); command.Parameters.AddWithValue("key", key!.Value); }
        await using var row = await command.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(row.GetGuid(0), row.GetGuid(1), row.GetInt64(2), Class(row, 3),
            row.GetBoolean(4), GuidValue(row, 5), row.IsDBNull(6) ? null : row.GetInt16(6),
            GuidValue(row, 7), GuidValue(row, 8), row.IsDBNull(9) ? null : row.GetInt64(9)) : null;
    }
    private static Guid? GuidValue(NpgsqlDataReader row, int index) => row.IsDBNull(index) ? null : row.GetGuid(index);
    private static RetentionRecordClass? Class(NpgsqlDataReader row, int index) => row.IsDBNull(index) ? null
        : row.GetString(index) == "OPERATIONAL_WORK" ? RetentionRecordClass.OperationalWork : Enum.Parse<RetentionRecordClass>(row.GetString(index), true);
    private void RequireScope(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Retention references require an owning transaction.");
    }
}
