using StrataAI.Application.Organizations;
using StrataAI.Application.Records;
using StrataAI.Infrastructure.WorkManagement;

namespace StrataAI.Infrastructure.Records;

internal sealed class InMemoryRetentionOrganizationDisposition(IOrganizationStore organizations,
    DemoWorkTransactionScope transaction, Func<InMemoryRetentionHoldStore> holds) : IRetentionOrganizationDisposition
{
    public async Task<RetentionDispositionAdmission> CheckAsync(Guid organization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (organization == Guid.Empty || !transaction.OwnsOrganizationCommand(organization)
            || (await organizations.FindOrganizationAsync(organization, ct)) is not { Status: OrganizationStatus.Active })
            return RetentionDispositionAdmission.Unavailable;
        return holds().HasAnyActiveDispositionHold(organization, ct)
            ? RetentionDispositionAdmission.Held : RetentionDispositionAdmission.Clear;
    }
}
