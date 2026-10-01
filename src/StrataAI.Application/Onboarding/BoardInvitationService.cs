using System.Security.Cryptography;
using System.Text.Json;
using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Onboarding;

public sealed class BoardInvitationService(IInvitationStore invitations, IOrganizationStore organizations,
    IWorkManagementStore work, IIdentityStore identities, IOrganizationUnitOfWork commands,
    ICommandActorAuthorization actors, ISecureTokenService tokens, IClock clock, IdentityPolicy policy,
    IWorkEventStore events, IInvitationMailPublisher? mail = null)
{
    public async Task<InvitationOperation<CreatedInvitation>> CreateAsync(Guid boardId, Guid actorId,
        string invitedEmail, BoardRole role, string correlationId, CancellationToken ct = default,
        Guid? idempotencyKey = null)
    {
        if (boardId == Guid.Empty || actorId == Guid.Empty)
            return InvitationOperation<CreatedInvitation>.Failure("board_not_found");
        // Only a routing hint. The real parent/Board and current authority are
        // locked and re-read in the Organization transaction below.
        var route = await work.FindBoardAsync(boardId, ct);
        if (route is null) return InvitationOperation<CreatedInvitation>.Failure("board_not_found");
        var result = await commands.ExecuteAsync(route.OrganizationId, actorId, null, false, async () =>
        {
            if (!await work.AcquireCommandScopeAsync(route.OrganizationId, actorId, boardId, ct))
                return OrganizationOperation<CreatedInvitation>.Failure("board_not_found");
            var board = await work.FindBoardAsync(boardId, ct);
            var organization = await organizations.FindOrganizationAsync(route.OrganizationId, ct);
            var issuer = await identities.FindUserByIdAsync(actorId, ct);
            var issuerMembership = await organizations.FindMembershipAsync(route.OrganizationId, actorId, ct);
            var boardMembership = await work.FindBoardMemberAsync(boardId, actorId, ct);
            if (board is null || organization is null || issuer is null
                || !BoardInvitationPolicy.CanIssue(organization, board, issuer, issuerMembership,
                    boardMembership, issuer, issuerMembership, BoardRole.Member, policy.RequireVerifiedEmail))
                return OrganizationOperation<CreatedInvitation>.Failure("board_not_found");

            if (!Enum.IsDefined(role)) return OrganizationOperation<CreatedInvitation>.Failure("invalid_invitation_role");
            var normalizedEmail = InvitationService.NormalizeEmail(invitedEmail);
            if (normalizedEmail is null || normalizedEmail.Length > 320)
                return OrganizationOperation<CreatedInvitation>.Failure("invalid_email");
            if (idempotencyKey == Guid.Empty)
                return OrganizationOperation<CreatedInvitation>.Failure("invalid_idempotency_key");
            var recipient = await identities.FindUserByNormalizedEmailAsync(normalizedEmail, ct);
            var recipientMembership = recipient is null ? null
                : await organizations.FindMembershipAsync(organization.Id, recipient.Id, ct);
            if (!BoardInvitationPolicy.CanIssue(organization, board, issuer, issuerMembership,
                boardMembership, recipient, recipientMembership, role, policy.RequireVerifiedEmail))
                return OrganizationOperation<CreatedInvitation>.Failure("board_not_found");

            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                operation = "BOARD_INVITATION_CREATE_V1", organizationId = organization.Id, boardId,
                actorId, normalizedEmail, role })));
            if (idempotencyKey is { } key)
            {
                var replay = await invitations.FindCreationReplayAsync(organization.Id, actorId, key, ct);
                if (!await actors.VerifyAsync(actorId, ct))
                    return OrganizationOperation<CreatedInvitation>.Failure("session_unavailable");
                if (replay is not null)
                {
                    if (replay.Fingerprint != fingerprint || replay.Invitation.BoardTarget != new BoardInvitationTarget(boardId, role))
                        return OrganizationOperation<CreatedInvitation>.Failure("idempotency_key_reused");
                    if (replay.Expired) return OrganizationOperation<CreatedInvitation>.Failure("idempotency_key_expired");
                    return OrganizationOperation<CreatedInvitation>.Success(new(replay.Invitation, ""));
                }
            }
            var now = clock.UtcNow;
            var invitationId = Guid.NewGuid();
            var deliveryToken = mail?.CreateToken(organization.Id, invitationId);
            var rawToken = deliveryToken?.RawToken ?? tokens.Generate();
            var invitation = await invitations.CreateAsync(new(invitationId, organization.Id, invitedEmail.Trim(),
                normalizedEmail, tokens.Hash(rawToken), InvitationSurface.Internal, "MEMBER", actorId,
                now, now.AddDays(7), null, null, OrganizationName: organization.Name,
                BoardTarget: new(boardId, role)), ct);
            if (deliveryToken is not null)
                await mail!.PublishAsync(invitation, deliveryToken.KeyId, correlationId, ct);
            await organizations.AppendAuditAsync(organization.Id, actorId, "BOARD_MEMBER_INVITED", "Invitation",
                invitation.Id, correlationId, ct);
            // A content-free Board invalidation does not disclose recipient email,
            // token, invitation ID or target role to other Board readers.
            await events.AppendAsync(new(Guid.NewGuid(), organization.Id, boardId, actorId,
                "BOARD_MEMBER_INVITED", "Board", boardId, board.Version, correlationId, now), ct);
            if (idempotencyKey is { } receiptKey)
                await invitations.SaveCreationReplayAsync(organization.Id, actorId, receiptKey, fingerprint, invitation.Id, ct);
            if (!await actors.VerifyAsync(actorId, ct))
                return OrganizationOperation<CreatedInvitation>.Failure("session_unavailable");
            return OrganizationOperation<CreatedInvitation>.Success(new(invitation, idempotencyKey is null && mail is null ? rawToken : ""));
        }, ct);
        return new(result.Succeeded, result.Value, result.ErrorCode switch {
            "organization_not_found" => "board_not_found",
            "organization_storage_unavailable" => "invitation_storage_unavailable",
            _ => result.ErrorCode,
        });
    }
}
