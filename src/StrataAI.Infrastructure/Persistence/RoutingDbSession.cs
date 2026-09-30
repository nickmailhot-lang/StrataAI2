using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

internal sealed class RoutingDbSession(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool ownsConnection) : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = connection;
    public NpgsqlTransaction? Transaction { get; } = transaction;
    public ValueTask DisposeAsync() => ownsConnection ? Connection.DisposeAsync() : ValueTask.CompletedTask;
}
