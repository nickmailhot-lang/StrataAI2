using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

internal enum RoutingLookup { Board, List, Card, Label, OrganizationUser, InvitationToken, InvitationRecipient, InvitationId }

internal sealed class RoutingDbSession(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool ownsConnection) : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = connection;
    public NpgsqlTransaction? Transaction { get; } = transaction;
    public async Task SetLookupAsync(RoutingLookup lookup, string key, CancellationToken cancellationToken)
    {
        if (Transaction is null) throw new InvalidOperationException("Routing discovery requires a transaction-local scope.");
        var kind = lookup switch
        {
            RoutingLookup.Board => "BOARD", RoutingLookup.List => "LIST", RoutingLookup.Card => "CARD",
            RoutingLookup.Label => "LABEL",
            RoutingLookup.OrganizationUser => "ORGANIZATION_USER", RoutingLookup.InvitationToken => "INVITATION_TOKEN",
            RoutingLookup.InvitationRecipient => "INVITATION_RECIPIENT", RoutingLookup.InvitationId => "INVITATION_ID",
            _ => throw new ArgumentOutOfRangeException(nameof(lookup))
        };
        await using var command = new NpgsqlCommand("SELECT set_config('app.route_kind',@kind,true), set_config('app.route_key',@key,true);", Connection, Transaction);
        command.Parameters.AddWithValue("kind", kind); command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        if (!ownsConnection) return;
        try { if (Transaction is not null) await Transaction.DisposeAsync(); }
        finally { await Connection.DisposeAsync(); }
    }
}
