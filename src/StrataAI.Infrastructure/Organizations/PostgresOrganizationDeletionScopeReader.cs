using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

public sealed class PostgresOrganizationDeletionScopeReader(PostgresConnectionFactory connections)
    : IOrganizationDeletionScopeReader
{
    public async Task<IReadOnlyList<Guid>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var query = new NpgsqlCommand("SELECT tenant_id FROM public.discover_organization_deletion_scopes(@after,@limit);", connection);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        query.Parameters.AddWithValue("limit", limit);
        List<Guid> result = [];
        await using var rows = await query.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken)) result.Add(rows.GetGuid(0));
        return result;
    }
}
