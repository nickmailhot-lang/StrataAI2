using Npgsql;

namespace StrataAI.Infrastructure.Persistence;

public sealed class TenantDbSession : IAsyncDisposable
{
    private readonly bool _ownsResources;
    internal TenantDbSession(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        bool ownsResources = true)
    {
        Connection = connection;
        Transaction = transaction;
        OrganizationId = organizationId;
        _ownsResources = ownsResources;
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction Transaction { get; }

    public Guid OrganizationId { get; }

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        _ownsResources ? Transaction.CommitAsync(cancellationToken) : Task.CompletedTask;

    internal TenantDbSession Borrow() => new(Connection, Transaction, OrganizationId, ownsResources: false);

    public async ValueTask DisposeAsync()
    {
        if (!_ownsResources) return;
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
