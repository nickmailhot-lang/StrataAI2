using Npgsql;
using StrataAI.Application.Records;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Records;

// Unregistered. Minimal owning-transaction evidence before first classification;
// the Application must authorize the actual Board and re-read after its locks.
internal sealed class PostgresRetentionProducerReferences(PostgresConnectionFactory connections) : IRetentionProducerReferences
{
    public async Task<RetentionProducerReference?> ReadAsync(Guid organization, RetentionProducerKind kind, Guid record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Retention producer requires an owning transaction.");
        if (record == Guid.Empty || kind is not (RetentionProducerKind.Card or RetentionProducerKind.CardComment or RetentionProducerKind.Attachment)) return null;
        // All query fragments are fixed mappings of the supported typed kinds.
        var source = kind switch
        {
            RetentionProducerKind.Card => "cards s",
            RetentionProducerKind.CardComment => "card_comments s",
            _ => "attachments s"
        };
        var parent = kind == RetentionProducerKind.Card ? "s.id" : "s.card_id";
        var state = kind == RetentionProducerKind.CardComment
            ? "CASE WHEN s.deleted_at IS NULL THEN 'ACTIVE' ELSE 'DELETED' END" : "s.lifecycle_state";
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        await using var command = new NpgsqlCommand("SELECT c.id,c.board_id,c.list_id,c.version,l.version,s.version,c.lifecycle_state,l.lifecycle_state," + state + ",s.created_at FROM " + source +
            " JOIN cards c ON c.tenant_id=s.tenant_id AND c.id=" + parent +
            " JOIN board_lists l ON(l.tenant_id,l.id,l.board_id)=(c.tenant_id,c.list_id,c.board_id) WHERE s.tenant_id=@tenant AND s.id=@record;",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("record", record);
        await using var row = await command.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(organization, record, kind, kind == RetentionProducerKind.Card ? null : row.GetGuid(0),
            row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetInt64(3), row.GetInt64(4), row.GetInt64(5),
            State(row, 6), State(row, 7), State(row, 8), row.GetFieldValue<DateTimeOffset>(9)) : null;
    }
    private static WorkItemLifecycleState State(NpgsqlDataReader row, int index) => Enum.Parse<WorkItemLifecycleState>(row.GetString(index), true);
}
