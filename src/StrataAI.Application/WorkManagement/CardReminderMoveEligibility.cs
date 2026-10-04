using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public interface ICardReminderMoveEligibility
{
    Task<bool> CanReceiveAsync(CardRecord card, Guid userId, CancellationToken ct);
}

// Called within the owning Card transaction after both Board gates are held.
public sealed class CardReminderMoveEligibility(IWorkManagementStore work, IOrganizationStore organizations,
    IIdentityStore identities, IdentityPolicy policy) : ICardReminderMoveEligibility
{
    public async Task<bool> CanReceiveAsync(CardRecord card, Guid userId, CancellationToken ct)
    {
        var board = await work.FindBoardAsync(card.BoardId, ct);
        var membership = await organizations.FindMembershipAsync(card.OrganizationId, userId, ct);
        var account = await identities.FindUserByIdAsync(userId, ct);
        return board is { LifecycleState: BoardLifecycleState.Active } && board.OrganizationId == card.OrganizationId
            && membership is { Active: true } && account is { Status: AccountStatus.Active }
            && (!policy.RequireVerifiedEmail || account.EmailVerified)
            && (membership.Role is OrganizationRole.Owner or OrganizationRole.Admin
                || board.Visibility is BoardVisibility.Public or BoardVisibility.Organization
                || await work.FindBoardMemberAsync(card.BoardId, userId, ct) is { Active: true });
    }
}
