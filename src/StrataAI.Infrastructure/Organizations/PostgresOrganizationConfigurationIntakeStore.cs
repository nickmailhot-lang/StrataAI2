using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class PostgresOrganizationConfigurationIntakeStore(PostgresConnectionFactory connections)
    : IOrganizationConfigurationIntakeStore
{
    public async Task<ConfigurationIntakeListSource?> ReadListsAsync(Guid organization, Guid board,
        string? afterRank, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !connections.HasCommandScope(organization))
            throw new InvalidOperationException("Configuration intake requires an owning Organization transaction.");
        await using var session = await connections.OpenTenantSessionAsync(organization, ct);
        ConfigurationIntakeBoard selected;
        await using (var command = new NpgsqlCommand("""
            SELECT id,name,version FROM boards
            WHERE tenant_id=@tenant AND id=@board AND lifecycle_state='ACTIVE'
              AND visibility IN ('PRIVATE','ORGANIZATION','PUBLIC') FOR SHARE;
            """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("tenant", organization); command.Parameters.AddWithValue("board", board);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            selected = new(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2));
        }
        await using var lists = new NpgsqlCommand("""
            SELECT id,name,version,rank FROM board_lists
            WHERE tenant_id=@tenant AND board_id=@board AND lifecycle_state='ACTIVE'
              AND (@after IS NULL OR rank>@after)
            ORDER BY rank LIMIT 51 FOR SHARE;
            """, session.Connection, session.Transaction);
        lists.Parameters.AddWithValue("tenant", organization); lists.Parameters.AddWithValue("board", board);
        lists.Parameters.AddWithValue("after", NpgsqlDbType.Text, (object?)afterRank ?? DBNull.Value);
        var items = new List<ConfigurationIntakeList>();
        await using var rows = await lists.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct)) items.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetInt64(2), rows.GetString(3)));
        return new(selected, items);
    }
}
