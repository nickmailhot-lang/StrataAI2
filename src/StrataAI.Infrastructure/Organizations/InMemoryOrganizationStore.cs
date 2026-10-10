using StrataAI.Application.Organizations;
using StrataAI.Application.Identity;
using StrataAI.Application.Common;
using StrataAI.Infrastructure.Onboarding;

namespace StrataAI.Infrastructure.Organizations;

internal sealed partial class InMemoryOrganizationStore(IIdentityStore identities, IdentityPolicy policy, IClock clock,
    IEnumerable<Func<IDemoInvitationAuditProjection>> invitationProjections,
    Func<InMemoryOrganizationMetadataJournal> metadataJournal,
    StrataAI.Infrastructure.WorkManagement.DemoWorkTransactionScope workScope) : IOrganizationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, OrganizationRecord> _organizations = [];
    private readonly Dictionary<(Guid OrganizationId, Guid UserId), OrganizationMembership> _members = [];
    private readonly Dictionary<Guid, (Guid Actor, DateTimeOffset At)> _deletionAttribution = [];
    private readonly Dictionary<Guid, DemoInvitationAudit> _deletionAudits = [];

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
        CancellationToken cancellationToken = default,
        string type = StrataAI.Domain.Organizations.OrganizationTypes.Default)
    {
        if (!StrataAI.Domain.Organizations.OrganizationTypes.IsSupported(type))
            throw new ArgumentException("Unsupported Organization type.", nameof(type));
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
                1) { Type = type };

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

            CaptureAuthorityProof(organizationId, "Organization", organizationId, "ORGANIZATION_CREATED", 1, createdAt);

            return Task.FromResult(organization);
        }
    }

    public Task<IReadOnlyList<Guid>> ListMembershipOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<Guid>>(_members.Values.Where(m => m.UserId == userId)
            .Select(m => m.OrganizationId).Distinct().Order().ToArray());
    }

    public async Task<OrganizationOperation<IReadOnlyList<Guid>>> ReadDirectoryRoutesAsync(Guid userId,
        Guid? after, CancellationToken cancellationToken = default) =>
        OrganizationOperation<IReadOnlyList<Guid>>.Success(
            await ListMembershipOrganizationIdsPageAsync(userId, after, cancellationToken));

    public Task<IReadOnlyList<Guid>> ListMembershipOrganizationIdsPageAsync(Guid userId, Guid? after, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<Guid>>(_members.Values.Where(m => m.UserId == userId)
            .Select(m => m.OrganizationId).Distinct().Where(id => after is null || id.CompareTo(after.Value) > 0)
            .Order().Take(51).ToArray());
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
                .Where(summary => summary.Organization.Status is not (OrganizationStatus.Deleting or OrganizationStatus.Deleted))
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
            CaptureAuthorityProof(organizationId, "Organization", organizationId, "ORGANIZATION_UPDATED", updated.Version, updatedAt);
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
            var member = _members[(organizationId, userId)];
            CaptureAuthorityProof(organizationId, "OrganizationMembership", member.Id, "ORGANIZATION_MEMBER_ADDED", member.Version, updatedAt);
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
            CaptureAuthorityProof(organizationId, "User", userId, "ORGANIZATION_MEMBER_REMOVED", membership.Version + 1, updatedAt, membership.Role);

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
                organization.Version != expectedVersion || organization.Status != OrganizationStatus.Active)
            {
                return Task.FromResult(false);
            }

            _organizations[organizationId] = organization with
            {
                Status = OrganizationStatus.Deleting,
                UpdatedAt = updatedAt,
                Version = organization.Version + 1,
            };
            CaptureAuthorityProof(organizationId, "Organization", organizationId, "ORGANIZATION_DELETION_REQUESTED", organization.Version + 1, updatedAt);
            return Task.FromResult(true);
        }
    }

    public async Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (eventType is not ("ORGANIZATION_CREATED" or "ORGANIZATION_MEMBER_ADDED" or "ORGANIZATION_MEMBER_INVITED" or "BOARD_MEMBER_INVITED" or "INVITATION_ACCEPTED" or "INVITATION_REVOKED"
            or "ORGANIZATION_UPDATED" or "ORGANIZATION_MEMBER_REMOVED" or "ORGANIZATION_MEMBER_LEFT" or "ORGANIZATION_DELETION_REQUESTED" or "ORGANIZATION_DELETED")) return;
        var auditTime = eventType == "ORGANIZATION_DELETED"
            ? RequireTransitionProof(organizationId, entityType, entityId, eventType).CreatedAt : clock.UtcNow;
        var audit = new DemoInvitationAudit(Guid.NewGuid(), organizationId, actorUserId, eventType, entityType, entityId, correlationId, auditTime);
        if (eventType is not ("ORGANIZATION_CREATED" or "ORGANIZATION_MEMBER_ADDED"))
            foreach (var projection in invitationProjections) await projection().AppendAsync(audit, cancellationToken);
        await metadataJournal().AppendAsync(audit, cancellationToken);
        if (eventType == "ORGANIZATION_DELETED") _deletionAudits.Add(organizationId, audit);
    }

    internal OrganizationMembership? FindMetadataMembership(Guid organizationId, Guid membershipId)
    {
        lock (_sync) return _members.Values.SingleOrDefault(member => member.OrganizationId == organizationId && member.Id == membershipId);
    }
    internal void CompleteAcceptedDeletion(Guid organization, Guid actor, Guid request, long version, DateTimeOffset at)
    {
        if (!workScope.OwnsAcceptedDeletion(organization, actor, request)) throw new OrganizationDeletionPublicationUnavailableException();
        lock (_sync)
        {
            if (!_organizations.TryGetValue(organization, out var parent) || parent.Status != OrganizationStatus.Deleting
                || parent.Version != version || at < parent.UpdatedAt || _deletionAttribution.ContainsKey(organization))
                throw new OrganizationDeletionPublicationUnavailableException();
            var terminalVersion = checked(version + 1);
            _organizations[organization] = parent with { Status = OrganizationStatus.Deleted, UpdatedAt = at, Version = terminalVersion };
            _deletionAttribution.Add(organization, (actor, at));
            CaptureAuthorityProof(organization, "Organization", organization, "ORGANIZATION_DELETED", terminalVersion, at);
        }
    }
    internal bool MatchesDeletionAttribution(Guid organization, Guid actor, DateTimeOffset at)
    { lock (_sync) return _deletionAttribution.GetValueOrDefault(organization) == (actor, at); }
    internal DemoInvitationAudit? ReadDeletionAudit(Guid organization, Guid actor, Guid request)
    {
        if (!workScope.OwnsAcceptedDeletion(organization, actor, request)) throw new OrganizationDeletionPublicationUnavailableException();
        return _deletionAudits.GetValueOrDefault(organization);
    }
}

internal sealed partial class InMemoryOrganizationStore : IDemoOrganizationTransactionParticipant
{
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            var organizations = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_organizations);
            var members = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_members);
            var authority = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_authorityProofs);
            var attribution = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_deletionAttribution);
            var audits = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_deletionAudits);
            return () => { lock (_sync) { organizations(); members(); authority(); attribution(); audits(); } };
        }
    }
}
