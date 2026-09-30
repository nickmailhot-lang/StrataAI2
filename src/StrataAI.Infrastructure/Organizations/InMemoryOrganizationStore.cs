using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, OrganizationRecord> _organizations = [];
    private readonly Dictionary<(Guid OrganizationId, Guid UserId), OrganizationMembership> _members = [];

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

    public Task<OrganizationRemoveMemberResult> RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_members.TryGetValue((organizationId, userId), out var membership) ||
                !membership.Active)
            {
                return Task.FromResult(OrganizationRemoveMemberResult.NotFound);
            }

            if (membership.Role == OrganizationRole.Owner)
            {
                var ownerCount = _members.Values.Count(
                    member =>
                        member.OrganizationId == organizationId &&
                        member.Active &&
                        member.Role == OrganizationRole.Owner);

                if (ownerCount <= 1)
                {
                    return Task.FromResult(OrganizationRemoveMemberResult.SoleOwner);
                }
            }

            _members[(organizationId, userId)] = membership with
            {
                Active = false,
                UpdatedAt = updatedAt,
                Version = membership.Version + 1,
            };

            return Task.FromResult(OrganizationRemoveMemberResult.Removed);
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
