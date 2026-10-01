namespace StrataAI.Application.Organizations;

public interface IOrganizationService
{
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
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>> ListBoardsAsync(
        Guid organizationId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation<bool>> RemoveMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation<bool>> LeaveAsync(
        Guid organizationId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation<bool>> MarkDeletingAsync(
        Guid organizationId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);
}
