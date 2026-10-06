namespace StrataAI.Application.Organizations;

public interface IOrganizationService
{
    Task<OrganizationOperation<OrganizationBoardDirectoryPage>> ListBoardsPageAsync(Guid organizationId,
        Guid actorUserId, Guid? after, CancellationToken cancellationToken = default);
    Task<OrganizationOperation<OrganizationDirectoryPage>> ListPageAsync(Guid actorUserId, Guid? after,
        CancellationToken cancellationToken = default);
    Task<OrganizationOperation<OrganizationSummary>> ReadAsync(Guid organizationId,
        Guid actorUserId, CancellationToken cancellationToken = default);
    Task<OrganizationOperation<OrganizationSurfaceAdmission>> ReadSurfaceAdmissionAsync(Guid organizationId,
        Guid actorUserId, bool portal, CancellationToken cancellationToken = default);
    Task<OrganizationOperation<OrganizationMemberReview>> ReviewMemberAsync(Guid organizationId,
        Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken = default);
    Task<OrganizationOperation<OrganizationMemberPage>> ListMembersAsync(Guid organizationId,
        Guid actorUserId, Guid? after, CancellationToken cancellationToken = default);
    Task<OrganizationOperation<OrganizationSummary>> CreateAsync(
        Guid actorUserId,
        string name,
        string? description,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationSummary>> ListAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation<OrganizationRecord>> UpdateAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        string? logoUrl,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default, Guid? idempotencyKey = null);

    Task<OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>> ListBoardsAsync(
        Guid organizationId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation<bool>> RemoveMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default,
        long? expectedVersion = null, Guid? idempotencyKey = null);

    Task<OrganizationOperation<bool>> LeaveAsync(
        Guid organizationId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default, Guid? idempotencyKey = null);

    Task<OrganizationOperation<bool>> MarkDeletingAsync(
        Guid organizationId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);
}
