using Npgsql;
using StrataAI.Application.Records;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Records;

// No hold reason, actor, receipt or history is read. The owning Work gate retains
// its Organization SHARE lock; apply/release owns that Organization FOR UPDATE.
// A fresh READ COMMITTED statement after admission observes the serialized head.
internal sealed class PostgresRetentionWorkDisposition(PostgresConnectionFactory connections) : IRetentionWorkDisposition
{
    public async Task<RetentionDispositionAdmission> CheckAsync(Guid organization, RetentionWorkDispositionKind kind,
        Guid record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || record == Guid.Empty || !Enum.IsDefined(kind)
            || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Disposition requires its owning tenant transaction.");
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var query = new NpgsqlCommand("""
            WITH root AS (
              SELECT id,'OPERATIONAL_WORK'::text AS record_class FROM boards
               WHERE tenant_id=@tenant AND id=@record AND @kind='Board' AND lifecycle_state<>'DELETED'
              UNION ALL SELECT id,'OPERATIONAL_WORK' FROM board_lists
               WHERE tenant_id=@tenant AND id=@record AND @kind='List' AND lifecycle_state<>'DELETED'
              UNION ALL SELECT id,'OPERATIONAL_WORK' FROM cards
               WHERE tenant_id=@tenant AND id=@record AND @kind='Card' AND lifecycle_state<>'DELETED'
              UNION ALL SELECT id,'OPERATIONAL_WORK' FROM card_comments
               WHERE tenant_id=@tenant AND id=@record AND @kind='CardComment' AND deleted_at IS NULL
              UNION ALL SELECT id,'DOCUMENT' FROM attachments
               WHERE tenant_id=@tenant AND id=@record AND @kind='Attachment' AND lifecycle_state<>'DELETED'
            ), family AS (
              SELECT id FROM cards WHERE tenant_id=@tenant
                AND ((@kind='Board' AND board_id=@record) OR (@kind='List' AND list_id=@record)
                  OR (@kind='Card' AND id=@record))
            ), affected AS (
              SELECT id,record_class FROM root
              UNION SELECT id,'OPERATIONAL_WORK' FROM family
              UNION SELECT c.id,'OPERATIONAL_WORK' FROM card_comments c JOIN family f ON f.id=c.card_id
                WHERE c.tenant_id=@tenant
              UNION SELECT a.id,'DOCUMENT' FROM attachments a JOIN family f ON f.id=a.card_id
                WHERE a.tenant_id=@tenant
            )
            SELECT EXISTS(SELECT FROM root), EXISTS(
              SELECT FROM record_retention_holds h WHERE h.tenant_id=@tenant AND h.status='ACTIVE'
                AND EXISTS(SELECT FROM affected a LEFT JOIN record_retention_sources s
                  ON s.tenant_id=@tenant AND s.record_class=a.record_class AND s.record_id=a.id
                  WHERE (h.record_class IS NULL OR h.record_class=a.record_class)
                    AND (h.record_id IS NULL OR h.record_id=a.id)
                    -- Unknown classification cannot prove exclusion from a
                    -- category-scoped hold. Do not fabricate a default category.
                    AND (h.category_key IS NULL OR s.category_key IS NULL OR h.category_key=s.category_key)));
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organization); query.Parameters.AddWithValue("record", record);
        query.Parameters.AddWithValue("kind", kind.ToString());
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || !reader.GetBoolean(0)) return RetentionDispositionAdmission.Unavailable;
        return reader.GetBoolean(1) ? RetentionDispositionAdmission.Held : RetentionDispositionAdmission.Clear;
    }
}
