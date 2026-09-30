using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

public sealed class TenantDbSession : IAsyncDisposable
{
    internal TenantDbSession(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction Transaction { get; }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        Transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
