using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Onboarding;

public sealed record IssuedInvitation(Guid Id, string Email, InvitationSurface Surface, string TargetRole,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt,
    string? DeliveryState, BoardInvitationTarget? BoardTarget = null);
public sealed record InvitationHistoryPage(IReadOnlyList<IssuedInvitation> Items, Guid? NextCursor);

public interface IInvitationHistoryStore
{
    Task<IReadOnlyList<IssuedInvitation>> ListAsync(Guid organizationId, Guid? after, CancellationToken cancellationToken, Guid? boardId = null);
}

public sealed class InvitationHistoryService(IInvitationHistoryStore store, IOrganizationStore organizations,
    IOrganizationUnitOfWork commands, ICommandActorAuthorization actors, IWorkManagementStore work,
    IIdentityStore identities, IdentityPolicy policy)
{
    public async Task<InvitationOperation<InvitationHistoryPage>> ListAsync(Guid organizationId, Guid actorId, Guid? after,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || after == Guid.Empty)
            return InvitationOperation<InvitationHistoryPage>.Failure("invalid_invitation_cursor");
        var result = await commands.ExecuteAsync(organizationId, actorId, null, false, async () =>
        {
            var membership = await organizations.FindMembershipAsync(organizationId, actorId, cancellationToken);
            if (membership is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin })
                return OrganizationOperation<InvitationHistoryPage>.Failure("organization_not_found");
            var rows = await store.ListAsync(organizationId, after, cancellationToken);
            if (!await actors.VerifyAsync(actorId, cancellationToken))
                return OrganizationOperation<InvitationHistoryPage>.Failure("session_unavailable");
            var items = rows.Take(50).ToArray();
            return OrganizationOperation<InvitationHistoryPage>.Success(new(items, rows.Count > 50 ? items[^1].Id : null));
        }, cancellationToken);
        return new(result.Succeeded, result.Value, result.ErrorCode == "organization_storage_unavailable"
            ? "invitation_storage_unavailable" : result.ErrorCode);
    }
    public async Task<InvitationOperation<InvitationHistoryPage>> ListBoardAsync(Guid boardId, Guid actorId,
        Guid? after, CancellationToken cancellationToken)
    {
        if (boardId == Guid.Empty || after == Guid.Empty)
            return InvitationOperation<InvitationHistoryPage>.Failure("invalid_invitation_cursor");
        var route = await work.FindBoardAsync(boardId, cancellationToken);
        if (route is null) return InvitationOperation<InvitationHistoryPage>.Failure("board_not_found");
        var result = await commands.ExecuteAsync(route.OrganizationId, actorId, null, false, async () =>
        {
            if (!await work.AcquireCommandScopeAsync(route.OrganizationId, actorId, boardId, cancellationToken))
                return OrganizationOperation<InvitationHistoryPage>.Failure("board_not_found");
            var board = await work.FindBoardAsync(boardId, cancellationToken);
            var organization = await organizations.FindOrganizationAsync(route.OrganizationId, cancellationToken);
            var actor = await identities.FindUserByIdAsync(actorId, cancellationToken);
            var membership = await organizations.FindMembershipAsync(route.OrganizationId, actorId, cancellationToken);
            var boardMember = await work.FindBoardMemberAsync(boardId, actorId, cancellationToken);
            if (board is null || organization is null || actor is null
                || !BoardInvitationPolicy.CanIssue(organization, board, actor, membership, boardMember,
                    actor, membership, BoardRole.Member, policy.RequireVerifiedEmail))
                return OrganizationOperation<InvitationHistoryPage>.Failure("board_not_found");
            var rows = await store.ListAsync(route.OrganizationId, after, cancellationToken, boardId);
            if (!await actors.VerifyAsync(actorId, cancellationToken))
                return OrganizationOperation<InvitationHistoryPage>.Failure("session_unavailable");
            var items = rows.Take(50).ToArray();
            return OrganizationOperation<InvitationHistoryPage>.Success(new(items, rows.Count > 50 ? items[^1].Id : null));
        }, cancellationToken);
        return new(result.Succeeded, result.Value, result.ErrorCode switch {
            "organization_storage_unavailable" => "invitation_storage_unavailable",
            "organization_not_found" => "board_not_found",
            _ => result.ErrorCode,
        });
    }

}
