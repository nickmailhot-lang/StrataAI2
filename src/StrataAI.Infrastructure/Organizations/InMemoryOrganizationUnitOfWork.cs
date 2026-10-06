using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationUnitOfWork(IOrganizationStore store, ICommandActorAuthorization actors,
    InMemoryAccountOrganizationGate gate, DemoWorkTransactionScope scope,
    IEnumerable<IDemoWorkTransactionParticipant> participants,
    IEnumerable<IDemoOrganizationTransactionParticipant> organizationParticipants) : IOrganizationUnitOfWork
{
    public async Task<OrganizationOperation<T>> ExecuteAsync<T>(
        Guid organizationId, Guid actorUserId, Guid? targetUserId, bool creating,
        Func<Task<OrganizationOperation<T>>> operation,
        CancellationToken cancellationToken = default, bool allowDeletionRecovery = false)
    {
        await gate.Commands.WaitAsync(cancellationToken);
        try
        {
            await gate.WorkCommands.WaitAsync(cancellationToken);
            try
            {
                using var owned = scope.Enter(organizationId);
                Action[] rollback = []; var committed = false;
                try
                {
                    // Both gates exclude account cleanup and Work mutations while
                    // capturing/restoring the cross-store Organization command.
                    rollback = organizationParticipants.Select(participant => participant.CaptureRollback())
                        .Concat(participants.Select(participant => participant.CaptureRollback())).ToArray();
                    if (!creating)
                    {
                        var parent = await store.FindOrganizationAsync(organizationId, cancellationToken);
                        if (parent is null || parent.Status != OrganizationStatus.Active
                            && !(allowDeletionRecovery && parent.Status == OrganizationStatus.Deleting))
                            return OrganizationOperation<T>.Failure("organization_not_found");
                    }
                    if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                        return OrganizationOperation<T>.Failure("session_unavailable");
                    var result = await operation();
                    if (!result.Succeeded) return result;
                    // A successful departure intentionally retires membership;
                    // the final fence checks the session rather than the old grant.
                    if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                        return OrganizationOperation<T>.Failure("session_unavailable");
                    cancellationToken.ThrowIfCancellationRequested();
                    committed = true; return result;
                }
                finally { if (!committed) foreach (var restore in rollback.Reverse()) restore(); }
            }
            finally { gate.WorkCommands.Release(); }
        }
        finally { gate.Commands.Release(); }
    }
}
