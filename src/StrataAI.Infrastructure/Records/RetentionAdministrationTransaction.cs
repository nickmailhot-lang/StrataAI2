using StrataAI.Application.Records;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

internal sealed class DemoRetentionAdministrationTransaction(DemoWorkTransactionScope scope)
    : IRetentionAdministrationTransaction
{
    public bool Owns(Guid organization) => organization != Guid.Empty && scope.Owns(organization);
}

internal sealed class PostgresRetentionAdministrationTransaction(PostgresConnectionFactory connections)
    : IRetentionAdministrationTransaction
{
    public bool Owns(Guid organization) => organization != Guid.Empty && connections.HasCommandScope(organization);
}
