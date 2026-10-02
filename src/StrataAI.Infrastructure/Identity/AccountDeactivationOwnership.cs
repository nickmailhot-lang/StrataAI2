using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.Identity;

internal sealed record AccountOwnershipPlan(Guid UserId, IReadOnlyList<Guid> OrganizationIds, bool Complete = true);
internal interface IAccountDeactivationOwnership
{
    Task<AccountOwnershipPlan> PrepareAsync(Guid userId, CancellationToken cancellationToken);
    Task<string?> CheckAsync(AccountOwnershipPlan plan, CancellationToken cancellationToken);
    Task CleanupAssignmentsAsync(AccountOwnershipPlan plan, string correlationId, CancellationToken cancellationToken);
}

// The caller holds the shared Demo identity/Organization command gate throughout.
// This is process-local admission, not PostgreSQL transaction/rollback evidence.
internal sealed class InMemoryAccountDeactivationOwnership(IOrganizationStore organizations,
    IIdentityStore identities, IdentityPolicy policy, IWorkManagementStore work, IWorkEventStore events, IClock clock) : IAccountDeactivationOwnership
{
    public async Task<AccountOwnershipPlan> PrepareAsync(Guid userId, CancellationToken cancellationToken) =>
        new(userId, await organizations.ListMembershipOrganizationIdsAsync(userId, cancellationToken));

    public async Task<string?> CheckAsync(AccountOwnershipPlan plan, CancellationToken cancellationToken)
    {
        foreach (var id in plan.OrganizationIds)
        {
            var membership = await organizations.FindMembershipAsync(id, plan.UserId, cancellationToken);
            if (membership is not { Active: true, Role: OrganizationRole.Owner }) continue;
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

    public async Task CleanupAssignmentsAsync(AccountOwnershipPlan plan, string correlationId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        foreach (var org in plan.OrganizationIds)
            foreach (var card in await work.RemoveOrganizationCardMemberAssignmentsAsync(org, plan.UserId, now, cancellationToken))
            {
                await work.AppendAuditAsync(org, plan.UserId, "CARD_MEMBER_REMOVED", "Card", card.Id, correlationId, cancellationToken);
                await events.AppendAsync(new(Guid.NewGuid(), org, card.BoardId, plan.UserId, "CARD_MEMBER_REMOVED", "Card", card.Id, card.Version, correlationId, now), cancellationToken);
            }
    }
}
