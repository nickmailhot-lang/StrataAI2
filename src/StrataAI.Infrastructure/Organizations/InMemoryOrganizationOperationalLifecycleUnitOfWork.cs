using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationOperationalLifecycleUnitOfWork(IOrganizationStore organizations,
    ICommandActorAuthorization actors, InMemoryAccountOrganizationGate gate, DemoWorkTransactionScope workScope,
    OrganizationOperationalLifecycleCommandScope lifecycleScope,
    IEnumerable<IDemoOrganizationTransactionParticipant> organizationParticipants,
    IEnumerable<IDemoWorkTransactionParticipant> workParticipants) : IOrganizationOperationalLifecycleUnitOfWork
{
    public async Task<OrganizationOperation<T>> ExecuteAsync<T>(Guid organization, Guid actor,
        Func<Task<OrganizationOperation<T>>> action, CancellationToken ct)
    {
        await gate.Commands.WaitAsync(ct);
        try
        {
            await gate.WorkCommands.WaitAsync(ct);
            try
            {
                using var work = workScope.Enter(organization, organizationCommand: true);
                using var lifecycle = lifecycleScope.Enter(organization, actor);
                var restore = organizationParticipants.Select(participant => participant.CaptureRollback())
                    .Concat(workParticipants.Select(participant => participant.CaptureRollback())).ToArray();
                var committed = false;
                try
                {
                    var parent = await organizations.FindOrganizationAsync(organization, ct);
                    if (parent?.Status is not (OrganizationStatus.Active or OrganizationStatus.Suspended or OrganizationStatus.Deactivated))
                        return OrganizationOperation<T>.Failure("organization_not_found");
                    if (!await actors.VerifyAsync(actor, ct)) return OrganizationOperation<T>.Failure("session_unavailable");
                    var result = await action();
                    if (!result.Succeeded) return result;
                    if (!await actors.VerifyAsync(actor, ct)) return OrganizationOperation<T>.Failure("session_unavailable");
                    ct.ThrowIfCancellationRequested(); committed = true; return result;
                }
                finally { if (!committed) foreach (var rollback in restore.Reverse()) rollback(); }
            }
            finally { gate.WorkCommands.Release(); }
        }
        finally { gate.Commands.Release(); }
    }
}
