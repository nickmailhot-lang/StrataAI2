using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.Onboarding;

public sealed record IssuedInvitation(Guid Id, string Email, InvitationSurface Surface, string TargetRole,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt,
    string? DeliveryState);
public sealed record InvitationHistoryPage(IReadOnlyList<IssuedInvitation> Items, Guid? NextCursor);

public interface IInvitationHistoryStore
{
    Task<IReadOnlyList<IssuedInvitation>> ListAsync(Guid organizationId, Guid? after, CancellationToken cancellationToken);
}

public sealed class InvitationHistoryService(IInvitationHistoryStore store, IOrganizationStore organizations,
    IOrganizationUnitOfWork commands, ICommandActorAuthorization actors)
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
}
