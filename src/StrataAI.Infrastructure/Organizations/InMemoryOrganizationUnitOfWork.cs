using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationUnitOfWork(IOrganizationStore store) : IOrganizationUnitOfWork
{
    private readonly SemaphoreSlim _commands = new(1, 1);

    public async Task<OrganizationOperation<T>> ExecuteAsync<T>(
        Guid organizationId, Guid actorUserId, Guid? targetUserId, bool creating,
        Func<Task<OrganizationOperation<T>>> operation,
        CancellationToken cancellationToken = default)
    {
        await _commands.WaitAsync(cancellationToken);
        try
        {
            if (!creating && (await store.FindOrganizationAsync(organizationId, cancellationToken))?.Status != OrganizationStatus.Active)
                return OrganizationOperation<T>.Failure("organization_not_found");
            return await operation();
        }
        finally { _commands.Release(); }
    }
}
