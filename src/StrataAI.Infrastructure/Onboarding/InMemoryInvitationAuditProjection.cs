using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;
using StrataAI.Infrastructure.Organizations;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed class InMemoryInvitationAuditProjection(InMemoryInvitationRecipientJournal journal,
    InMemoryInvitationStore invitations, IOrganizationStore organizations, IWorkManagementStore work,
    IIdentityStore identities, IClock clock) : IDemoInvitationAuditProjection
{
    public async Task AppendAsync(DemoInvitationAudit audit, CancellationToken cancellationToken)
    {
        if (audit.EventType is "ORGANIZATION_UPDATED" or "ORGANIZATION_MEMBER_REMOVED" or "ORGANIZATION_MEMBER_LEFT" or "ORGANIZATION_DELETION_REQUESTED")
        {
            if (audit.Id == Guid.Empty || audit.ActorId == Guid.Empty || audit.CorrelationId.Length is < 1 or > 64
                || await identities.FindUserByIdAsync(audit.ActorId, cancellationToken) is not { Status: AccountStatus.Active })
                throw new InvalidOperationException("Organization authority source is invalid.");
            var authorityProof = ((InMemoryOrganizationStore)organizations).RequireAuthorityProof(audit.OrganizationId, audit.EntityType, audit.EntityId, audit.EventType);
            var actor = await organizations.FindMembershipAsync(audit.OrganizationId, audit.ActorId, cancellationToken);
            var selfDeparture = audit.ActorId == audit.EntityId && actor is { Active: false }
                && actor.Version == authorityProof.Version && actor.UpdatedAt == authorityProof.CreatedAt
                && (audit.EventType == "ORGANIZATION_MEMBER_LEFT"
                    || audit.EventType == "ORGANIZATION_MEMBER_REMOVED" && authorityProof.PreviousRole is OrganizationRole.Owner or OrganizationRole.Admin);
            if (audit.EventType == "ORGANIZATION_MEMBER_LEFT" ? !selfDeparture
                : actor is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin } && !selfDeparture)
                throw new InvalidOperationException("Organization authority source actor is unavailable.");
            if (audit.EventType == "ORGANIZATION_DELETION_REQUESTED")
            {
                var parent = await organizations.FindOrganizationAsync(audit.OrganizationId, cancellationToken);
                if (actor is not { Active: true, Role: OrganizationRole.Owner } || authorityProof.EntityType != "Organization"
                    || authorityProof.EntityId != audit.OrganizationId || parent is not { Status: OrganizationStatus.Deleting }
                    || parent.Version != authorityProof.Version || parent.UpdatedAt != authorityProof.CreatedAt)
                    throw new InvalidOperationException("Organization deletion request source is unproven.");
            }
            journal.PublishAuthoritySource(audit, authorityProof, cancellationToken);
            journal.SimulateAuthorityDelivery(audit, invitations, cancellationToken);
            return;
        }
        if (audit.EntityType != "Invitation" || audit.Id == Guid.Empty || audit.ActorId == Guid.Empty
            || audit.CorrelationId.Length is < 1 or > 64)
            throw new InvalidOperationException("Invitation recipient source is invalid.");
        var row = await invitations.FindByIdAsync(audit.OrganizationId, audit.EntityId, cancellationToken)
            ?? throw new InvalidOperationException("Invitation recipient subject is unavailable.");
        var kind = audit.EventType is "ORGANIZATION_MEMBER_INVITED" or "BOARD_MEMBER_INVITED" ? "INVITATION_CREATED" : audit.EventType;
        var proof = journal.RequireProof(row, kind);
        if (await organizations.FindOrganizationAsync(row.OrganizationId, cancellationToken) is not { Status: OrganizationStatus.Active }
            || await identities.FindUserByIdAsync(audit.ActorId, cancellationToken) is not { Status: AccountStatus.Active })
            throw new InvalidOperationException("Invitation recipient source authority is unavailable.");
        if (kind == "INVITATION_ACCEPTED")
        {
            var recipient = await identities.FindUserByIdAsync(audit.ActorId, cancellationToken);
            if (row.AcceptedByUserId != audit.ActorId || row.AcceptedAt is null || row.RevokedAt is not null
                || recipient is not { EmailVerified: true } || recipient.EmailNormalized != row.EmailNormalized
                || await identities.FindUserByIdAsync(row.CreatedByUserId, cancellationToken) is not { Status: AccountStatus.Active })
                throw new InvalidOperationException("Invitation recipient acceptance actor is unavailable.");
            var membership = await organizations.FindMembershipAsync(row.OrganizationId, audit.ActorId, cancellationToken);
            if (row.BoardTarget is { } target)
            {
                var board = await work.FindBoardAsync(target.BoardId, cancellationToken);
                var grant = await work.FindBoardMemberAsync(target.BoardId, audit.ActorId, cancellationToken);
                if (membership is not { Active: true } || board is not { LifecycleState: BoardLifecycleState.Active }
                    || board.OrganizationId != row.OrganizationId || grant is not { Active: true }
                    || grant.Role != target.Role && !(target.Role == BoardRole.Member && grant.Role == BoardRole.Admin))
                    throw new InvalidOperationException("Invitation recipient Board grant is unavailable.");
            }
            else if (row.Surface == InvitationSurface.Internal)
            {
                if (membership is not { Active: true } || Role(membership.Role) != row.TargetRole)
                    throw new InvalidOperationException("Invitation recipient Internal grant is unavailable.");
            }
            else if (!invitations.HasPortalRelationship(row.OrganizationId, audit.ActorId, row.TargetRole))
                throw new InvalidOperationException("Invitation recipient Portal grant is unavailable.");
        }
        else
        {
            if (kind == "INVITATION_CREATED" && (row.CreatedByUserId != audit.ActorId || proof.Version != 1
                || row.AcceptedAt is not null || row.RevokedAt is not null || row.ExpiresAt <= clock.UtcNow
                || (row.BoardTarget is null) != (audit.EventType == "ORGANIZATION_MEMBER_INVITED"))
                || kind == "INVITATION_REVOKED" && (row.RevokedAt is null || row.AcceptedAt is not null))
                throw new InvalidOperationException("Invitation recipient lifecycle source is invalid.");
            var member = await organizations.FindMembershipAsync(row.OrganizationId, audit.ActorId, cancellationToken);
            if (member is not { Active: true }) throw new InvalidOperationException("Invitation recipient administrator is unavailable.");
            if (row.BoardTarget is { } target)
            {
                var board = await work.FindBoardAsync(target.BoardId, cancellationToken);
                var grant = await work.FindBoardMemberAsync(target.BoardId, audit.ActorId, cancellationToken);
                if (board is not { LifecycleState: BoardLifecycleState.Active } || board.OrganizationId != row.OrganizationId
                    || member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin)
                        && !(member.Role == OrganizationRole.Member && grant is { Active: true, Role: BoardRole.Admin }))
                    throw new InvalidOperationException("Invitation recipient Board administrator is unavailable.");
            }
            else if (member.Role is not (OrganizationRole.Owner or OrganizationRole.Admin)
                || kind == "INVITATION_CREATED" && row.Surface == InvitationSurface.Internal && row.TargetRole == "OWNER"
                    && member.Role != OrganizationRole.Owner)
                throw new InvalidOperationException("Invitation recipient administrator is unavailable.");
        }
        journal.Publish(audit, proof, cancellationToken);
    }
    private static string Role(OrganizationRole role) => role switch
    { OrganizationRole.Owner => "OWNER", OrganizationRole.Admin => "ADMIN", _ => "MEMBER" };
}
