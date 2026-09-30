namespace StrataAI.Application.Organizations;

public interface IOrganizationStore
{
    Task<OrganizationRecord> CreateOrganizationAsync(
        Guid actorUserId,
        Guid organizationId,
        string name,
        string? description,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

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
