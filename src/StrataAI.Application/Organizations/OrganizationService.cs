using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Organizations;

public sealed class OrganizationService(
    IOrganizationStore store,
    IWorkManagementStore workStore,
    IClock clock) : IOrganizationService
{
    public async Task<OrganizationOperation<OrganizationSummary>> CreateAsync(
        Guid actorUserId,
        string name,
        string? description,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 160)
        {
            return OrganizationOperation<OrganizationSummary>.Failure(
                "invalid_organization_name");
        }

        var organization = await store.CreateOrganizationAsync(
            actorUserId,
            Guid.NewGuid(),
            normalizedName,
            NormalizeOptional(description),
            clock.UtcNow,
            cancellationToken);

        await store.AppendAuditAsync(
            organization.Id,
            actorUserId,
            "ORGANIZATION_CREATED",
            "Organization",
            organization.Id,
            correlationId,
            cancellationToken);

        return OrganizationOperation<OrganizationSummary>.Success(
            new OrganizationSummary(organization, OrganizationRole.Owner));
    }

    public Task<IReadOnlyList<OrganizationSummary>> ListAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        store.ListOrganizationsForUserAsync(actorUserId, cancellationToken);

    public async Task<OrganizationOperation<OrganizationRecord>> UpdateAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        string? logoUrl,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (!CanAdminister(membership))
        {
            return OrganizationOperation<OrganizationRecord>.Failure(
                "organization_not_found");
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 160)
        {
            return OrganizationOperation<OrganizationRecord>.Failure(
                "invalid_organization_name");
        }

        var updated = await store.UpdateOrganizationAsync(
            organizationId,
            normalizedName,
            NormalizeOptional(description),
            NormalizeOptional(logoUrl),
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return OrganizationOperation<OrganizationRecord>.Failure(
                "version_conflict");
        }

        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_UPDATED",
            "Organization",
            organizationId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<OrganizationRecord>.Success(updated);
    }

    public async Task<OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>> ListBoardsAsync(
        Guid organizationId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null || !membership.Active)
        {
            return OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>.Failure(
                "organization_not_found");
        }

        var boards = await workStore.ListVisibleBoardsAsync(
            organizationId,
            actorUserId,
            membership.Role is OrganizationRole.Owner or OrganizationRole.Admin,
            cancellationToken);

        return OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>.Success(
            boards);
    }

    public async Task<OrganizationOperation<bool>> RemoveMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (!CanAdminister(actor))
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        var target = await store.FindMembershipAsync(
            organizationId,
            targetUserId,
            cancellationToken);

        if (target is null || !target.Active)
        {
            return OrganizationOperation<bool>.Failure("member_not_found");
        }

        if (actor!.Role == OrganizationRole.Admin &&
            target.Role == OrganizationRole.Owner)
        {
            return OrganizationOperation<bool>.Failure("insufficient_permission");
        }

        var result = await store.RemoveMemberAsync(
            organizationId,
            targetUserId,
            clock.UtcNow,
            cancellationToken);

        if (result == OrganizationRemoveMemberResult.SoleOwner)
        {
            return OrganizationOperation<bool>.Failure("sole_owner");
        }

        if (result == OrganizationRemoveMemberResult.NotFound)
        {
            return OrganizationOperation<bool>.Failure("member_not_found");
        }

        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_MEMBER_REMOVED",
            "User",
            targetUserId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<bool>.Success(true);
    }

    public async Task<OrganizationOperation<bool>> LeaveAsync(
        Guid organizationId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null || !membership.Active)
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        var result = await store.RemoveMemberAsync(
            organizationId,
            actorUserId,
            clock.UtcNow,
            cancellationToken);

        if (result == OrganizationRemoveMemberResult.SoleOwner)
        {
            return OrganizationOperation<bool>.Failure("sole_owner");
        }

        if (result != OrganizationRemoveMemberResult.Removed)
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_MEMBER_LEFT",
            "User",
            actorUserId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<bool>.Success(true);
    }

    public async Task<OrganizationOperation<bool>> MarkDeletingAsync(
        Guid organizationId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null ||
            !membership.Active ||
            membership.Role != OrganizationRole.Owner)
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        var changed = await store.MarkDeletingAsync(
            organizationId,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (!changed)
        {
            return OrganizationOperation<bool>.Failure("version_conflict");
        }

        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_DELETION_REQUESTED",
            "Organization",
            organizationId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<bool>.Success(true);
    }

    private static bool CanAdminister(OrganizationMembership? membership) =>
        membership is
        {
            Active: true,
            Role: OrganizationRole.Owner or OrganizationRole.Admin,
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
