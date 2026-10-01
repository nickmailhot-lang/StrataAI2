using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationUnitOfWork(IOrganizationStore store, ICommandActorAuthorization actors,
    InMemoryAccountOrganizationGate gate) : IOrganizationUnitOfWork
{
    private readonly SemaphoreSlim _commands = gate.Commands;

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
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return OrganizationOperation<T>.Failure("session_unavailable");
            return await operation();
        }
        finally { _commands.Release(); }
    }
}
