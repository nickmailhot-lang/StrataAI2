using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

// Audience selection is server-owned. Both modes require the owning Work read
// transaction and final permission/session proof before transport disclosure.
public sealed class PostgresOrganizationBoardEventReader(PostgresConnectionFactory connections,
    OrganizationBoardAudience audience = OrganizationBoardAudience.ArchiveAdministration) : IOrganizationBoardEventReader
{
    private readonly OrganizationBoardAudience _audience = Enum.IsDefined(audience) ? audience : throw new ArgumentOutOfRangeException(nameof(audience));
    public async Task<OrganizationBoardCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty) return null;
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        // Do not lock e: grant writers already hold their membership tuple before
        // revising the epoch. Locking epoch first would invert that lock order.
        await using var command = new NpgsqlCommand("""
            SELECT m.id,o.version,e.generation,e.permission_revision,e.reader_revision FROM organizations o
            JOIN organization_members m ON m.tenant_id=o.id
            JOIN organization_board_directory_epochs e ON e.tenant_id=m.tenant_id AND e.user_id=m.user_id
            WHERE o.id=@tenant AND o.status='ACTIVE' AND m.user_id=@actor AND m.status='ACTIVE'
            FOR SHARE OF o,m;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        OrganizationBoardCursorBinding? binding = null;
        await using (var result = await command.ExecuteReaderAsync(cancellationToken))
            if (await result.ReadAsync(cancellationToken))
                binding = new(organizationId, actorId, result.GetGuid(0), result.GetInt64(1), result.GetGuid(2), result.GetInt64(3),
                    _audience, _audience == OrganizationBoardAudience.BoardDiscovery ? result.GetInt64(4) : 0);
        await session.CommitAsync(cancellationToken);
        return binding;
    }

    public async Task<long> GetHeadAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT last_sequence FROM organization_board_event_streams WHERE tenant_id=@tenant;",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId);
        var head = await command.ExecuteScalarAsync(cancellationToken) is long value ? value : 0;
        await session.CommitAsync(cancellationToken);
        return head;
    }

    public async Task<WorkOperation<OrganizationBoardEventPage>> ReadAsync(Guid organizationId,
        Guid actorId, long since, int limit, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || organizationId == Guid.Empty)
            return WorkOperation<OrganizationBoardEventPage>.Failure("organization_not_found");
        if (since < 0 || limit is < 1 or > 100)
            return WorkOperation<OrganizationBoardEventPage>.Failure("invalid_sync_cursor");
        await using var session = await connections.OpenTenantSessionAsync(organizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", organizationId);
            command.Parameters.AddWithValue("actor", actorId);
            return command;
        }
        bool administrator;
        await using (var scope = Query("""
            SELECT m.role IN ('OWNER','ADMIN') FROM organizations o
            JOIN organization_members m ON m.tenant_id=o.id
            WHERE o.id=@tenant AND o.status='ACTIVE' AND m.user_id=@actor AND m.status='ACTIVE'
            FOR SHARE OF o,m;
            """))
        {
            if (await scope.ExecuteScalarAsync(cancellationToken) is not bool allowed)
                return WorkOperation<OrganizationBoardEventPage>.Failure("organization_not_found");
            administrator = allowed;
        }
        long published;
        await using (var head = Query("SELECT last_sequence FROM organization_board_event_streams WHERE tenant_id=@tenant;"))
            published = await head.ExecuteScalarAsync(cancellationToken) is long value ? value : 0;
        if (!administrator && _audience == OrganizationBoardAudience.BoardDiscovery)
        {
            var candidates = new List<OrganizationBoardEventCandidate>();
            // Split the two eligibility paths so private grants use an actual
            // INNER JOIN tuple lock. Locking a nullable LEFT JOIN cannot prove
            // private admission after a wait. Each path is bounded before merge.
            foreach (var privateBoard in new[] { false, true })
            {
                var membership = privateBoard ? """
                    JOIN board_members m ON m.tenant_id=b.tenant_id AND m.board_id=b.id
                        AND m.user_id=@actor AND m.status='ACTIVE'
                    """ : "";
                var visibility = privateBoard ? "b.visibility='PRIVATE'" : "b.visibility IN ('PUBLIC','ORGANIZATION')";
                var lockTargets = privateBoard ? "b,m" : "b";
                await using var readerQuery = Query($"""
                    SELECT j.sequence,e.event_id,e.board_id,e.event_type,e.entity_version,e.created_at,e.ready_at IS NOT NULL
                    FROM organization_board_events j
                    JOIN work_events e ON e.tenant_id=j.tenant_id AND e.event_id=j.event_id
                    JOIN boards b ON b.tenant_id=e.tenant_id AND b.id=e.board_id
                    {membership}
                    WHERE j.tenant_id=@tenant AND j.sequence>@since AND j.sequence<=@published AND {visibility}
                    ORDER BY j.sequence LIMIT @limit FOR SHARE OF {lockTargets};
                    """);
                readerQuery.Parameters.AddWithValue("since", since);
                readerQuery.Parameters.AddWithValue("published", published);
                readerQuery.Parameters.AddWithValue("limit", limit + 1);
                await using var result = await readerQuery.ExecuteReaderAsync(cancellationToken);
                while (await result.ReadAsync(cancellationToken))
                    candidates.Add(new(result.GetInt64(0), result.GetGuid(1), result.GetGuid(2), result.GetString(3),
                        result.GetInt64(4), result.GetFieldValue<DateTimeOffset>(5), result.GetBoolean(6)));
            }
            var readerPage = OrganizationBoardEventWindow.Build(since, published, limit,
                candidates.OrderBy(row => row.Sequence).Take(limit + 1).ToArray());
            await session.CommitAsync(cancellationToken);
            return WorkOperation<OrganizationBoardEventPage>.Success(readerPage);
        }
        // Inner JOIN the actual grant tuple, rather than a stale EXISTS snapshot
        // after a Board lock wait. Archived/deleted Boards retain body-free
        // lifecycle invalidations for their current administrators.
        var grantJoin = administrator ? "" : """
            JOIN board_members m ON m.tenant_id=b.tenant_id AND m.board_id=b.id
                AND m.user_id=@actor AND m.status='ACTIVE' AND m.role='ADMIN'
            """;
        var locks = administrator ? "b" : "b,m";
        await using var read = Query($"""
            SELECT j.sequence,e.event_id,e.board_id,e.event_type,e.entity_version,e.created_at,e.ready_at IS NOT NULL
            FROM organization_board_events j
            JOIN work_events e ON e.tenant_id=j.tenant_id AND e.event_id=j.event_id
            JOIN boards b ON b.tenant_id=e.tenant_id AND b.id=e.board_id
            {grantJoin}
            WHERE j.tenant_id=@tenant AND j.sequence>@since AND j.sequence<=@published
            ORDER BY j.sequence LIMIT @limit FOR SHARE OF {locks};
            """);
        read.Parameters.AddWithValue("since", since);
        read.Parameters.AddWithValue("published", published);
        read.Parameters.AddWithValue("limit", limit + 1);
        var rows = new List<OrganizationBoardEventCandidate>();
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                    reader.GetInt64(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetBoolean(6)));
        var page = OrganizationBoardEventWindow.Build(since, published, limit, rows);
        await session.CommitAsync(cancellationToken);
        return WorkOperation<OrganizationBoardEventPage>.Success(page);
    }
}
