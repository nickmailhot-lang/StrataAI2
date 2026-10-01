using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Identity;

internal sealed record AccountOwnershipPlan(Guid UserId, IReadOnlyList<Guid> OrganizationIds, bool Complete = true);
internal interface IAccountDeactivationOwnership
{
    Task<AccountOwnershipPlan> PrepareAsync(Guid userId, CancellationToken cancellationToken);
    Task<string?> CheckAsync(AccountOwnershipPlan plan, CancellationToken cancellationToken);
}

// The caller holds the shared Demo identity/Organization command gate throughout.
// This is process-local admission, not PostgreSQL transaction/rollback evidence.
internal sealed class InMemoryAccountDeactivationOwnership(IOrganizationStore organizations,
    IIdentityStore identities, IdentityPolicy policy) : IAccountDeactivationOwnership
{
    public async Task<AccountOwnershipPlan> PrepareAsync(Guid userId, CancellationToken cancellationToken) =>
        new(userId, (await organizations.ListOrganizationsForUserAsync(userId, cancellationToken))
            .Where(item => item.Role == OrganizationRole.Owner).Select(item => item.Organization.Id).ToArray());

    public async Task<string?> CheckAsync(AccountOwnershipPlan plan, CancellationToken cancellationToken)
    {
        foreach (var id in plan.OrganizationIds)
        {
            var organization = await organizations.FindOrganizationAsync(id, cancellationToken);
            if (organization is null || organization.Status == OrganizationStatus.Deleting) continue;
            var remaining = false;
            foreach (var owner in await organizations.ListActiveOwnerUserIdsAsync(id, cancellationToken))
            {
                if (owner == plan.UserId) continue;
                var user = await identities.FindUserByIdAsync(owner, cancellationToken);
                if (user is { Status: AccountStatus.Active } && (!policy.RequireVerifiedEmail || user.EmailVerified))
                { remaining = true; break; }
            }
            if (!remaining) return "organization_owner_required";
        }
        return null;
    }
}
