using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

public sealed class TenantDbSession : IAsyncDisposable
{
    internal TenantDbSession(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId)
    {
        Connection = connection;
        Transaction = transaction;
        OrganizationId = organizationId;
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction Transaction { get; }

    public Guid OrganizationId { get; }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        Transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
