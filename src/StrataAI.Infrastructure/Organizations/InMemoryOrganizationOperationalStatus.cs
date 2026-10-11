using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Organizations;

internal sealed partial class InMemoryOrganizationStore
{
    // The complete source/receipt/audit is owned by the lifecycle store and both
    // global Demo gates. This changes no Board/List/Card/membership history.
    internal bool ChangeOperationalStatus(long expectedVersion, OrganizationOperationalLifecycleTransition source,
        OrganizationOperationalLifecycleCommandScope lifecycle)
    {
        if (!workScope.OwnsOrganizationCommand(source.OrganizationId) || !lifecycle.Owns(source.OrganizationId, source.ActorId))
            throw new InvalidOperationException("An owning lifecycle transaction is required.");
        lock (_sync)
        {
            if (!_organizations.TryGetValue(source.OrganizationId, out var parent)
                || parent.Version != expectedVersion || parent.Status != source.PreviousStatus)
                return false;
            _organizations[source.OrganizationId] = parent with
            {
                Status = source.Status, Version = source.Version, UpdatedAt = source.CreatedAt,
            };
            return true;
        }
    }
}
