namespace StrataAI.Application.Organizations;

public interface IOrganizationStore
{
    // Internal lifecycle admission hints, including inactive memberships. They
    // are not an authorized Organization-directory response.
    Task<IReadOnlyList<Guid>> ListMembershipOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    // Bounded internal routing hints (51 rows, UUID seek). Includes inactive
    // memberships; callers must freshly authorize every tenant before exposure.
    Task<OrganizationOperation<IReadOnlyList<Guid>>> ReadDirectoryRoutesAsync(Guid userId, Guid? after,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListMembershipOrganizationIdsPageAsync(Guid userId, Guid? after, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationMemberSummary>> ListActiveMembersAsync(Guid organizationId,
        Guid? after, CancellationToken cancellationToken = default, Guid? userId = null,
        IReadOnlyCollection<Guid>? userIds = null);
    Task<IReadOnlyList<Guid>> ListActiveOwnerUserIdsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationRecord?> FindOrganizationAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    Task<OrganizationRecord> CreateOrganizationAsync(
        Guid actorUserId,
        Guid organizationId,
        string name,
        string? description,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default,
        string type = StrataAI.Domain.Organizations.OrganizationTypes.Default);

    Task<IReadOnlyList<OrganizationSummary>> ListOrganizationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMembership?> FindMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OrganizationRecord?> UpdateOrganizationAsync(
        Guid organizationId,
        string name,
        string? description,
        string? logoUrl,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task AddOrRestoreMemberAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<OrganizationRemoveMemberResult> RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task<bool> MarkDeletingAsync(
        Guid organizationId,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public enum OrganizationRemoveMemberResult
{
    Removed,
    NotFound,
    SoleOwner,
}
