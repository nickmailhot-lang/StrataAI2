using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationStore(IIdentityStore identities, IdentityPolicy policy) : IOrganizationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, OrganizationRecord> _organizations = [];
    private readonly Dictionary<(Guid OrganizationId, Guid UserId), OrganizationMembership> _members = [];

    public async Task<IReadOnlyList<OrganizationMemberSummary>> ListActiveMembersAsync(Guid organizationId,
        Guid? after, CancellationToken cancellationToken = default, Guid? userId = null,
        IReadOnlyCollection<Guid>? userIds = null)
    {
        if (userIds is { Count: > 51 }) throw new ArgumentException("A profile batch may contain at most 51 users.", nameof(userIds));
        var requested = userIds?.ToHashSet();
        OrganizationMembership[] members;
        lock (_sync)
            members = _members.Values.Where(member => member.OrganizationId == organizationId && member.Active
                && (userId is null || member.UserId == userId)
                && (requested is null || requested.Contains(member.UserId))
                && (after is null || string.CompareOrdinal(member.UserId.ToString("N"), after.Value.ToString("N")) > 0))
                .OrderBy(member => member.UserId.ToString("N"), StringComparer.Ordinal).ToArray();
        var result = new List<OrganizationMemberSummary>();
        foreach (var member in members)
        {
            var user = await identities.FindUserByIdAsync(member.UserId, cancellationToken);
            if (user is null) continue;
            result.Add(new(member.Id, user.Id, user.DisplayName, user.Email, member.Role, user.Status,
                user.EmailVerified, member.Role == OrganizationRole.Owner && user.Status == AccountStatus.Active
                    && (!policy.RequireVerifiedEmail || user.EmailVerified), member.CreatedAt, member.UpdatedAt, member.Version));
            if (result.Count == 51) break;
        }
        return result;
    }

    public Task<IReadOnlyList<Guid>> ListActiveOwnerUserIdsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<Guid>>(_members.Values.Where(member => member.OrganizationId == organizationId
                && member.Active && member.Role == OrganizationRole.Owner).Select(member => member.UserId).Order().ToArray());
    }

    public Task<OrganizationRecord?> FindOrganizationAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _organizations.TryGetValue(organizationId, out var organization);
            return Task.FromResult(organization);
        }
    }

    public Task<OrganizationRecord> CreateOrganizationAsync(
        Guid actorUserId,
        Guid organizationId,
        string name,
        string? description,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var organization = new OrganizationRecord(
                organizationId,
                name,
                description,
                null,
                actorUserId,
                OrganizationStatus.Active,
                createdAt,
                createdAt,
                1);

            _organizations[organizationId] = organization;
            _members[(organizationId, actorUserId)] =
                new OrganizationMembership(
                    Guid.NewGuid(),
                    organizationId,
                    actorUserId,
                    OrganizationRole.Owner,
                    true,
                    createdAt,
                    createdAt,
                    1);

            return Task.FromResult(organization);
        }
    }

    public Task<IReadOnlyList<OrganizationSummary>> ListOrganizationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var result = _members.Values
                .Where(member => member.UserId == userId && member.Active)
                .Select(member =>
                    new OrganizationSummary(
                        _organizations[member.OrganizationId],
                        member.Role))
                .OrderBy(summary => summary.Organization.Name)
                .ToArray();

            return Task.FromResult<IReadOnlyList<OrganizationSummary>>(result);
        }
    }

    public Task<OrganizationMembership?> FindMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _members.TryGetValue((organizationId, userId), out var membership);
            return Task.FromResult<OrganizationMembership?>(membership);
        }
    }

    public Task<OrganizationRecord?> UpdateOrganizationAsync(
        Guid organizationId,
        string name,
        string? description,
        string? logoUrl,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_organizations.TryGetValue(organizationId, out var organization) ||
                organization.Version != expectedVersion)
            {
                return Task.FromResult<OrganizationRecord?>(null);
            }

            var updated = organization with
            {
                Name = name,
                Description = description,
                LogoUrl = logoUrl,
                UpdatedAt = updatedAt,
                Version = organization.Version + 1,
            };
            _organizations[organizationId] = updated;
            return Task.FromResult<OrganizationRecord?>(updated);
        }
    }

    public Task AddOrRestoreMemberAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_members.TryGetValue((organizationId, userId), out var existing))
            {
                _members[(organizationId, userId)] = existing with
                {
                    Role = role,
                    Active = true,
                    UpdatedAt = updatedAt,
                    Version = existing.Version + 1,
                };
            }
            else
            {
                _members[(organizationId, userId)] = new OrganizationMembership(
                    Guid.NewGuid(),
                    organizationId,
                    userId,
                    role,
                    true,
                    updatedAt,
                    updatedAt,
                    1);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<OrganizationRemoveMemberResult> RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        OrganizationMembership membership;
        Guid[] alternatives;
        lock (_sync)
        {
            if (!_members.TryGetValue((organizationId, userId), out var found) || !found.Active)
                return OrganizationRemoveMemberResult.NotFound;
            membership = found;
            alternatives = _members.Values.Where(member => member.OrganizationId == organizationId && member.UserId != userId
                && member.Active && member.Role == OrganizationRole.Owner).Select(member => member.UserId).ToArray();
        }
        // Organization service commands own the shared Demo account/Organization
        // gate, so account deactivation cannot race this active-owner decision.
        if (membership.Role == OrganizationRole.Owner)
        {
            var remaining = false;
            foreach (var id in alternatives)
            {
                var user = await identities.FindUserByIdAsync(id, cancellationToken);
                if (user is { Status: AccountStatus.Active } && (!policy.RequireVerifiedEmail || user.EmailVerified))
                { remaining = true; break; }
            }
            if (!remaining) return OrganizationRemoveMemberResult.SoleOwner;
        }
        lock (_sync)
        {
            if (!_members.TryGetValue((organizationId, userId), out var current) || !current.Active || current.Version != membership.Version)
                return OrganizationRemoveMemberResult.NotFound;
            _members[(organizationId, userId)] = membership with
            {
                Active = false,
                UpdatedAt = updatedAt,
                Version = membership.Version + 1,
            };

            return OrganizationRemoveMemberResult.Removed;
        }
    }

    public Task<bool> MarkDeletingAsync(
        Guid organizationId,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_organizations.TryGetValue(organizationId, out var organization) ||
                organization.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _organizations[organizationId] = organization with
            {
                Status = OrganizationStatus.Deleting,
                UpdatedAt = updatedAt,
                Version = organization.Version + 1,
            };
            return Task.FromResult(true);
        }
    }

    public Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
