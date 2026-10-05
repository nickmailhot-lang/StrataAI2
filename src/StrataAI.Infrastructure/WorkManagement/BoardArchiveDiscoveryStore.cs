using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<IReadOnlyList<ArchivedBoardSummary>> ListArchivedBoardsAsync(Guid organizationId, Guid actorId,
        bool organizationAdministrator, Guid? after, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<ArchivedBoardSummary>>(_boards.Values
            .Where(b => b.OrganizationId == organizationId && b.LifecycleState == BoardLifecycleState.Archived &&
                (after is null || b.Id.CompareTo(after.Value) > 0) && (organizationAdministrator ||
                    _members.TryGetValue((b.Id, actorId), out var member) && member is { Active: true, Role: BoardRole.Admin }))
            .OrderBy(b => b.Id).Take(51).Select(b => new ArchivedBoardSummary(b.Id, b.OrganizationId, b.Name, b.Version, b.ArchivedAt)).ToArray());
    }
}

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<ArchivedBoardSummary>> ListArchivedBoardsAsync(Guid organizationId, Guid actorId,
        bool organizationAdministrator, Guid? after, CancellationToken cancellationToken = default)
    {
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        // Lock the qualifying membership tuple as well as the Board. After a
        // Board-gate wait PostgreSQL must recheck a changed membership tuple;
        // an EXISTS subquery alone can retain its pre-wait permission snapshot.
        var sql = organizationAdministrator ? """
            SELECT b.id,b.tenant_id,b.name,b.version,b.archived_at FROM boards b
            WHERE b.tenant_id=@tenant AND b.lifecycle_state='ARCHIVED' AND (@after IS NULL OR b.id>@after)
            ORDER BY b.id LIMIT 51 FOR SHARE OF b;
            """ : """
            SELECT b.id,b.tenant_id,b.name,b.version,b.archived_at FROM boards b
            JOIN board_members m ON m.tenant_id=b.tenant_id AND m.board_id=b.id
            WHERE b.tenant_id=@tenant AND b.lifecycle_state='ARCHIVED' AND (@after IS NULL OR b.id>@after)
                AND m.user_id=@actor AND m.status='ACTIVE' AND m.role='ADMIN'
            ORDER BY b.id LIMIT 51 FOR SHARE OF b,m;
            """;
        await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, after is null ? DBNull.Value : after.Value);
        var rows = new List<ArchivedBoardSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        return rows;
    }
}
