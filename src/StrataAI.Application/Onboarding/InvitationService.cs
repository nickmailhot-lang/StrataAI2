using System.Net.Mail;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.Onboarding;

public sealed class InvitationService(
    IInvitationStore invitationStore,
    IOrganizationStore organizationStore,
    IIdentityStore identityStore,
    ISecureTokenService tokens,
    IClock clock) : IInvitationService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<InvitationOperation<CreatedInvitation>> CreateAsync(
        Guid organizationId,
        Guid actorUserId,
        string invitedEmail,
        InvitationSurface surface,
        string targetRole,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await organizationStore.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null ||
            !membership.Active ||
            membership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
        {
            return InvitationOperation<CreatedInvitation>.Failure(
                "organization_not_found");
        }

        var emailNormalized = NormalizeEmail(invitedEmail);
        if (emailNormalized is null)
        {
            return InvitationOperation<CreatedInvitation>.Failure("invalid_email");
        }

        var role = NormalizeTargetRole(surface, targetRole);
        if (role is null)
        {
            return InvitationOperation<CreatedInvitation>.Failure(
                "invalid_invitation_role");
        }

        var now = clock.UtcNow;
        var rawToken = tokens.Generate();
        var invitation = new InvitationRecord(
            Guid.NewGuid(),
            organizationId,
            invitedEmail.Trim(),
            emailNormalized,
            tokens.Hash(rawToken),
            surface,
            role,
            actorUserId,
            now,
            now.Add(InvitationLifetime),
            null,
            null);

        await invitationStore.CreateAsync(invitation, cancellationToken);
        await organizationStore.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_MEMBER_INVITED",
            "Invitation",
            invitation.Id,
            correlationId,
            cancellationToken);

        return InvitationOperation<CreatedInvitation>.Success(
            new CreatedInvitation(invitation, rawToken));
    }

    public async Task<IReadOnlyList<PendingInvitation>> ListPendingAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await identityStore.FindUserByIdAsync(
            actorUserId,
            cancellationToken);

        if (user is null || !user.EmailVerified)
        {
            return [];
        }

        return await invitationStore.ListPendingForEmailAsync(
            user.EmailNormalized,
            clock.UtcNow,
            cancellationToken);
    }

    public async Task<InvitationOperation<AcceptedInvitation>> AcceptAsync(
        Guid actorUserId,
        string rawToken,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                "invalid_or_expired_invitation");
        }

        var user = await identityStore.FindUserByIdAsync(
            actorUserId,
            cancellationToken);

        if (user is null ||
            user.Status != AccountStatus.Active ||
            !user.EmailVerified)
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                "account_unavailable");
        }

        var tokenHash = tokens.Hash(rawToken);
        var invitation = await invitationStore.FindActiveByTokenHashAsync(
            tokenHash,
            clock.UtcNow,
            cancellationToken);

        if (invitation is null ||
            !string.Equals(
                invitation.EmailNormalized,
                user.EmailNormalized,
                StringComparison.Ordinal))
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                "invalid_or_expired_invitation");
        }

        var result = await invitationStore.AcceptAsync(
            tokenHash,
            actorUserId,
            user.EmailNormalized,
            clock.UtcNow,
            cancellationToken);

        if (!result.Succeeded || result.Invitation is null)
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                result.ErrorCode ?? "invalid_or_expired_invitation");
        }

        await organizationStore.AppendAuditAsync(
            result.Invitation.OrganizationId,
            actorUserId,
            "INVITATION_ACCEPTED",
            "Invitation",
            result.Invitation.Id,
            correlationId,
            cancellationToken);

        return InvitationOperation<AcceptedInvitation>.Success(
            new AcceptedInvitation(
                result.Invitation.Id,
                result.Invitation.OrganizationId,
                result.Invitation.Surface,
                result.Invitation.TargetRole));
    }

    public async Task<InvitationOperation<bool>> RevokeAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid invitationId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await organizationStore.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null ||
            !membership.Active ||
            membership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
        {
            return InvitationOperation<bool>.Failure("organization_not_found");
        }

        if (!await invitationStore.RevokeAsync(
                organizationId,
                invitationId,
                clock.UtcNow,
                cancellationToken))
        {
            return InvitationOperation<bool>.Failure("invitation_not_found");
        }

        await organizationStore.AppendAuditAsync(
            organizationId,
            actorUserId,
            "INVITATION_REVOKED",
            "Invitation",
            invitationId,
            correlationId,
            cancellationToken);

        return InvitationOperation<bool>.Success(true);
    }

    private static string? NormalizeEmail(string email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            !MailAddress.TryCreate(trimmed, out var parsed) ||
            !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }

    private static string? NormalizeTargetRole(
        InvitationSurface surface,
        string targetRole)
    {
        var role = targetRole?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return surface switch
        {
            InvitationSurface.Internal when
                role is "OWNER" or "ADMIN" or "MEMBER" => role,
            InvitationSurface.Portal when
                role is "OWNER"
                    or "CO_OWNER"
                    or "TENANT"
                    or "OCCUPANT"
                    or "AUTHORIZED_REPRESENTATIVE"
                    or "OTHER" => role,
            _ => null,
        };
    }
}
