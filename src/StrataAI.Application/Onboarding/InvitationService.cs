using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Onboarding;

public sealed class InvitationService(
    IInvitationStore invitationStore,
    IOrganizationStore organizationStore,
    IIdentityStore identityStore,
    ISecureTokenService tokens,
    IClock clock,
    IOrganizationUnitOfWork unitOfWork, IIdentityUnitOfWork identityCommands, ICommandActorAuthorization actors,
    IWorkManagementStore work, IdentityPolicy policy, IWorkEventStore events,
    IInvitationMailPublisher? mail = null) : IInvitationService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public Task<InvitationOperation<CreatedInvitation>> CreateAsync(
        Guid organizationId, Guid actorUserId, string invitedEmail, InvitationSurface surface,
        string targetRole, string correlationId, CancellationToken cancellationToken = default, Guid? idempotencyKey = null) =>
        ExecuteAsync(organizationId, actorUserId, null,
            () => CreateCoreAsync(organizationId, actorUserId, invitedEmail, surface, targetRole, correlationId, cancellationToken, idempotencyKey), cancellationToken);

    public Task<InvitationOperation<bool>> RevokeAsync(
        Guid organizationId, Guid actorUserId, Guid invitationId, string correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(organizationId, actorUserId, null,
            () => RevokeCoreAsync(organizationId, actorUserId, invitationId, correlationId, cancellationToken), cancellationToken);

    public async Task<InvitationOperation<PendingInvitation>> ReviewTokenAsync(Guid actorUserId, string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 512)
            return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
        var user = await identityStore.FindUserByIdAsync(actorUserId, cancellationToken);
        if (user is not { Status: AccountStatus.Active, EmailVerified: true })
            return InvitationOperation<PendingInvitation>.Failure("account_unavailable");
        var hash = tokens.Hash(rawToken);
        // The first lookup is a routing hint. Re-read all disclosure authority after parent/identity locks.
        var route = await invitationStore.FindActiveByTokenHashAsync(hash, clock.UtcNow, cancellationToken);
        if (route is null || route.EmailNormalized != user.EmailNormalized)
            return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
        var result = await ExecuteAsync(route.OrganizationId, actorUserId, route.CreatedByUserId, async () =>
        {
            if (route.BoardTarget is { } routedTarget
                && !await work.AcquireCommandScopeAsync(route.OrganizationId, route.CreatedByUserId, routedTarget.BoardId, cancellationToken))
                return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
            var currentUser = await identityStore.FindUserByIdAsync(actorUserId, cancellationToken);
            if (currentUser is not { Status: AccountStatus.Active, EmailVerified: true })
                return InvitationOperation<PendingInvitation>.Failure("account_unavailable");
            var invitation = await invitationStore.FindActiveByTokenHashAsync(hash, clock.UtcNow, cancellationToken);
            if (invitation is null || invitation.BoardTarget != route.BoardTarget || invitation.OrganizationId != route.OrganizationId
                || invitation.CreatedByUserId != route.CreatedByUserId || invitation.EmailNormalized != currentUser.EmailNormalized
                || invitation.AcceptedAt is not null || invitation.RevokedAt is not null)
                return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
            var issuerAccount = await identityStore.FindUserByIdAsync(invitation.CreatedByUserId, cancellationToken);
            var issuer = await organizationStore.FindMembershipAsync(invitation.OrganizationId, invitation.CreatedByUserId, cancellationToken);
            var organization = await organizationStore.FindOrganizationAsync(invitation.OrganizationId, cancellationToken);
            if (organization is not { Status: OrganizationStatus.Active } || invitation.ExpiresAt <= clock.UtcNow)
                return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
            BoardRecord? targetBoard = null;
            if (invitation.BoardTarget is { } target)
            {
                targetBoard = await work.FindBoardAsync(target.BoardId, cancellationToken);
                var boardIssuer = await work.FindBoardMemberAsync(target.BoardId, invitation.CreatedByUserId, cancellationToken);
                var recipientMembership = await organizationStore.FindMembershipAsync(invitation.OrganizationId, actorUserId, cancellationToken);
                if (targetBoard is null || issuerAccount is null
                    || invitation.Surface != InvitationSurface.Internal || invitation.TargetRole != "MEMBER"
                    || !BoardInvitationPolicy.CanIssue(organization, targetBoard, issuerAccount, issuer, boardIssuer,
                        currentUser, recipientMembership, target.Role, policy.RequireVerifiedEmail))
                    return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
            }
            else if (issuerAccount is not { Status: AccountStatus.Active }
                || issuer is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin }
                || (invitation.Surface == InvitationSurface.Internal && invitation.TargetRole == "OWNER" && issuer.Role != OrganizationRole.Owner))
                return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return InvitationOperation<PendingInvitation>.Failure("session_unavailable");
            if (invitation.ExpiresAt <= clock.UtcNow)
                return InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation");
            return InvitationOperation<PendingInvitation>.Success(new(invitation.Id, invitation.OrganizationId,
                invitation.Surface, invitation.TargetRole, invitation.ExpiresAt, organization.Name, invitation.BoardTarget, targetBoard?.Name));
        }, cancellationToken);
        return result.ErrorCode == "organization_not_found"
            ? InvitationOperation<PendingInvitation>.Failure("invalid_or_expired_invitation") : result;
    }

    public async Task<InvitationOperation<AcceptedInvitation>> AcceptAsync(
        Guid actorUserId, string rawToken, string correlationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        var user = await identityStore.FindUserByIdAsync(actorUserId, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active || !user.EmailVerified)
            return InvitationOperation<AcceptedInvitation>.Failure("account_unavailable");
        var invitation = await invitationStore.FindActiveByTokenHashAsync(tokens.Hash(rawToken), clock.UtcNow, cancellationToken);
        if (invitation is null || invitation.EmailNormalized != user.EmailNormalized)
            return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        var result = await ExecuteAsync(invitation.OrganizationId, actorUserId, invitation.CreatedByUserId,
            () => AcceptCoreAsync(actorUserId, tokens.Hash(rawToken), null, invitation, correlationId, cancellationToken), cancellationToken);
        return result.ErrorCode == "organization_not_found"
            ? InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation") : result;
    }

    public async Task<InvitationOperation<AcceptedInvitation>> AcceptPendingAsync(Guid actorUserId, Guid invitationId,
        string correlationId, CancellationToken cancellationToken = default)
    {
        if (invitationId == Guid.Empty) return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        var user = await identityStore.FindUserByIdAsync(actorUserId, cancellationToken);
        if (user is not { Status: AccountStatus.Active, EmailVerified: true })
            return InvitationOperation<AcceptedInvitation>.Failure("account_unavailable");
        // Routing is a hint only. Tenant admission and the exact verified email are checked again after locks.
        var invitation = await invitationStore.FindActiveByIdForEmailAsync(invitationId, actorUserId, user.EmailNormalized, clock.UtcNow, cancellationToken);
        if (invitation is null) return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        var result = await ExecuteAsync(invitation.OrganizationId, actorUserId, invitation.CreatedByUserId,
            () => AcceptCoreAsync(actorUserId, null, invitationId, invitation, correlationId, cancellationToken), cancellationToken);
        return result.ErrorCode == "organization_not_found"
            ? InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation") : result;
    }

    private async Task<InvitationOperation<T>> ExecuteAsync<T>(
        Guid organizationId, Guid actorUserId, Guid? targetUserId,
        Func<Task<InvitationOperation<T>>> operation, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteAsync(organizationId, actorUserId, targetUserId, false, async () =>
        {
            var invitationResult = await operation();
            return new OrganizationOperation<T>(invitationResult.Succeeded, invitationResult.Value, invitationResult.ErrorCode);
        }, cancellationToken);
        return new InvitationOperation<T>(result.Succeeded, result.Value,
            result.ErrorCode == "organization_storage_unavailable" ? "invitation_storage_unavailable" : result.ErrorCode);
    }

    private async Task<InvitationOperation<CreatedInvitation>> CreateCoreAsync(
        Guid organizationId,
        Guid actorUserId,
        string invitedEmail,
        InvitationSurface surface,
        string targetRole,
        string correlationId,
        CancellationToken cancellationToken = default,
        Guid? idempotencyKey = null)
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

        if (surface == InvitationSurface.Internal && role == "OWNER" && membership.Role != OrganizationRole.Owner)
            return InvitationOperation<CreatedInvitation>.Failure("insufficient_permission");

        if (idempotencyKey == Guid.Empty) return InvitationOperation<CreatedInvitation>.Failure("invalid_idempotency_key");
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            operation = "INVITATION_CREATE_V1", organizationId, actorUserId, emailNormalized, surface, role })));
        if (idempotencyKey is { } key)
        {
            var replay = await invitationStore.FindCreationReplayAsync(organizationId, actorUserId, key, cancellationToken);
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return InvitationOperation<CreatedInvitation>.Failure("session_unavailable");
            if (replay is not null)
            {
                if (replay.Invitation.BoardTarget is not null) return InvitationOperation<CreatedInvitation>.Failure("idempotency_key_reused");
                if (replay.Fingerprint != fingerprint) return InvitationOperation<CreatedInvitation>.Failure("idempotency_key_reused");
                if (replay.Expired) return InvitationOperation<CreatedInvitation>.Failure("idempotency_key_expired");
                return InvitationOperation<CreatedInvitation>.Success(new(replay.Invitation, ""));
            }
        }

        var now = clock.UtcNow;
        var organization = await organizationStore.FindOrganizationAsync(organizationId, cancellationToken);
        if (organization is null) return InvitationOperation<CreatedInvitation>.Failure("organization_not_found");
        var invitationId = Guid.NewGuid();
        var deliveryToken = mail?.CreateToken(organizationId, invitationId);
        var rawToken = deliveryToken?.RawToken ?? tokens.Generate();
        var invitation = new InvitationRecord(
            invitationId,
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
            null, OrganizationName: organization.Name);

        invitation = await invitationStore.CreateAsync(invitation, cancellationToken);
        if (deliveryToken is not null)
            await mail!.PublishAsync(invitation, deliveryToken.KeyId, correlationId, cancellationToken);
        await organizationStore.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_MEMBER_INVITED",
            "Invitation",
            invitation.Id,
            correlationId,
            cancellationToken);

        if (idempotencyKey is { } completedKey)
            await invitationStore.SaveCreationReplayAsync(organizationId, actorUserId, completedKey, fingerprint, invitation.Id, cancellationToken);

        return InvitationOperation<CreatedInvitation>.Success(
            new CreatedInvitation(invitation, idempotencyKey is null && mail is null ? rawToken : ""));
    }

    public async Task<InvitationOperation<PendingInvitationPage>> ListPendingAsync(
        Guid actorUserId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        if (after == Guid.Empty) return InvitationOperation<PendingInvitationPage>.Failure("invalid_invitation_cursor");
        var result = await identityCommands.ExecuteAsync(actorUserId, async () =>
        {
            var user = await identityStore.FindUserByIdAsync(actorUserId, cancellationToken);
            if (user is not { Status: AccountStatus.Active, EmailVerified: true })
                return IdentityOperation<PendingInvitationPage>.Failure("account_unavailable");
            var rows = await invitationStore.ListPendingForEmailAsync(user.EmailNormalized, clock.UtcNow, after, cancellationToken);
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return IdentityOperation<PendingInvitationPage>.Failure("session_unavailable");
            var items = rows.Take(50).ToArray();
            return IdentityOperation<PendingInvitationPage>.Success(new PendingInvitationPage(items, rows.Count > 50 ? items[^1].Id : null));
        }, cancellationToken);
        return new InvitationOperation<PendingInvitationPage>(result.Succeeded, result.Value,
            result.ErrorCode == "identity_storage_unavailable" ? "invitation_storage_unavailable" : result.ErrorCode);
    }

    private async Task<InvitationOperation<AcceptedInvitation>> AcceptCoreAsync(
        Guid actorUserId,
        string? tokenHash,
        Guid? invitationId,
        InvitationRecord route,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) && invitationId is null)
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                "invalid_or_expired_invitation");
        }

        if (route.BoardTarget is { } routedTarget
            && !await work.AcquireCommandScopeAsync(route.OrganizationId, route.CreatedByUserId, routedTarget.BoardId, cancellationToken))
            return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
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

        var invitation = invitationId is Guid id
            ? await invitationStore.FindActiveByIdForEmailAsync(id, actorUserId, user.EmailNormalized, clock.UtcNow, cancellationToken)
            : await invitationStore.FindActiveByTokenHashAsync(tokenHash!, clock.UtcNow, cancellationToken);

        if (invitation is null || invitation.Id != route.Id || invitation.OrganizationId != route.OrganizationId
            || invitation.CreatedByUserId != route.CreatedByUserId || invitation.BoardTarget != route.BoardTarget ||
            !string.Equals(
                invitation.EmailNormalized,
                user.EmailNormalized,
                StringComparison.Ordinal))
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                "invalid_or_expired_invitation");
        }

        var issuerAccount = await identityStore.FindUserByIdAsync(invitation.CreatedByUserId, cancellationToken);
        var issuer = await organizationStore.FindMembershipAsync(invitation.OrganizationId, invitation.CreatedByUserId, cancellationToken);
        BoardRecord? targetBoard = null;
        if (invitation.BoardTarget is { } target)
        {
            targetBoard = await work.FindBoardAsync(target.BoardId, cancellationToken);
            var organization = await organizationStore.FindOrganizationAsync(invitation.OrganizationId, cancellationToken);
            var boardIssuer = await work.FindBoardMemberAsync(target.BoardId, invitation.CreatedByUserId, cancellationToken);
            var recipientMembership = await organizationStore.FindMembershipAsync(invitation.OrganizationId, actorUserId, cancellationToken);
            if (targetBoard is null || organization is null || issuerAccount is null
                || invitation.Surface != InvitationSurface.Internal || invitation.TargetRole != "MEMBER"
                || !BoardInvitationPolicy.CanIssue(organization, targetBoard, issuerAccount, issuer, boardIssuer,
                    user, recipientMembership, target.Role, policy.RequireVerifiedEmail))
                return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        }
        else if (issuerAccount is not { Status: AccountStatus.Active } ||
            issuer is not { Active: true, Role: OrganizationRole.Owner or OrganizationRole.Admin } ||
            (invitation.Surface == InvitationSurface.Internal && invitation.TargetRole == "OWNER" && issuer.Role != OrganizationRole.Owner))
            return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        if (invitation.ExpiresAt <= clock.UtcNow)
            return InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        if (!await actors.VerifyAsync(actorUserId, cancellationToken))
            return InvitationOperation<AcceptedInvitation>.Failure("session_unavailable");
        if (invitation.AcceptedAt is not null)
            return invitationId is not null && invitation.AcceptedByUserId == actorUserId
                ? InvitationOperation<AcceptedInvitation>.Success(new AcceptedInvitation(invitation.Id, invitation.OrganizationId,
                    invitation.Surface, invitation.TargetRole, invitation.BoardTarget))
                : InvitationOperation<AcceptedInvitation>.Failure("invalid_or_expired_invitation");
        if (invitation.BoardTarget is null && invitation.Surface == InvitationSurface.Internal && invitation.TargetRole != "OWNER")
        {
            var existing = await organizationStore.FindMembershipAsync(invitation.OrganizationId, actorUserId, cancellationToken);
            if (existing is { Active: true, Role: OrganizationRole.Owner })
                return InvitationOperation<AcceptedInvitation>.Failure("ownership_change_requires_confirmation");
        }

        var priorBoardMembership = invitation.BoardTarget is { } beforeTarget
            ? await work.FindBoardMemberAsync(beforeTarget.BoardId, actorUserId, cancellationToken) : null;
        var result = await invitationStore.AcceptAsync(
            invitation.TokenHash,
            actorUserId,
            user.EmailNormalized,
            clock.UtcNow,
            cancellationToken);

        if (!result.Succeeded || result.Invitation is null)
        {
            return InvitationOperation<AcceptedInvitation>.Failure(
                result.ErrorCode ?? "invalid_or_expired_invitation");
        }

        if (!await actors.VerifyAsync(actorUserId, cancellationToken))
            return InvitationOperation<AcceptedInvitation>.Failure("session_unavailable");

        await organizationStore.AppendAuditAsync(
            result.Invitation.OrganizationId,
            actorUserId,
            "INVITATION_ACCEPTED",
            "Invitation",
            result.Invitation.Id,
            correlationId,
            cancellationToken);

        if (targetBoard is not null)
        {
            var currentMembership = await work.FindBoardMemberAsync(targetBoard.Id, actorUserId, cancellationToken);
            if (currentMembership is not { Active: true })
                return InvitationOperation<AcceptedInvitation>.Failure("invitation_storage_unavailable");
            var memberEvent = priorBoardMembership is not { Active: true } ? "BOARD_MEMBER_ADDED"
                : priorBoardMembership.Role != currentMembership.Role ? "BOARD_MEMBER_ROLE_CHANGED" : null;
            if (memberEvent is not null)
            {
                await organizationStore.AppendAuditAsync(invitation.OrganizationId, actorUserId, memberEvent,
                    "Board", targetBoard.Id, correlationId, cancellationToken);
                await events.AppendAsync(new(Guid.NewGuid(), invitation.OrganizationId, targetBoard.Id, actorUserId,
                    memberEvent, "Board", targetBoard.Id, targetBoard.Version, correlationId, clock.UtcNow), cancellationToken);
            }
            await events.AppendAsync(new(Guid.NewGuid(), invitation.OrganizationId, targetBoard.Id, actorUserId,
                "INVITATION_ACCEPTED", "Board", targetBoard.Id, targetBoard.Version, correlationId, clock.UtcNow), cancellationToken);
        }
        return InvitationOperation<AcceptedInvitation>.Success(
            new AcceptedInvitation(
                result.Invitation.Id,
                result.Invitation.OrganizationId,
                result.Invitation.Surface,
                result.Invitation.TargetRole, result.Invitation.BoardTarget));
    }

    private async Task<InvitationOperation<bool>> RevokeCoreAsync(
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

    internal static string? NormalizeEmail(string email)
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
