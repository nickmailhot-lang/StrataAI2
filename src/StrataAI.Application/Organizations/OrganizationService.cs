using StrataAI.Application.Common;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Application.Organizations;

public sealed class OrganizationService(
    IOrganizationStore store,
    IWorkManagementStore workStore,
    IClock clock,
    IOrganizationUnitOfWork unitOfWork,
    StrataAI.Application.Identity.ICommandActorAuthorization actors,
    StrataAI.Application.Onboarding.IInvitationStore invitations, IWorkEventStore workEvents,
    CardReminderContainerScheduling reminders, IOrganizationMetadataReplayStore metadataReplays,
    IOrganizationDepartureReplayStore departureReplays, IOrganizationRemovalReplayStore removalReplays) : IOrganizationService
{
    public async Task<OrganizationOperation<OrganizationDirectoryPage>> ListPageAsync(Guid actorUserId,
        Guid? after, CancellationToken cancellationToken = default)
    {
        if (!await actors.VerifyAsync(actorUserId, cancellationToken))
            return OrganizationOperation<OrganizationDirectoryPage>.Failure("session_unavailable");
        if (after == Guid.Empty)
            return OrganizationOperation<OrganizationDirectoryPage>.Failure("invalid_organization_cursor");
        var routing = await store.ReadDirectoryRoutesAsync(actorUserId, after, cancellationToken);
        if (!routing.Succeeded)
            return OrganizationOperation<OrganizationDirectoryPage>.Failure(routing.ErrorCode!);
        var routes = routing.Value!;
        var candidates = routes.Take(50).ToArray();
        var items = new List<OrganizationSummary>(candidates.Length);
        foreach (var organizationId in candidates)
        {
            // Routing hints never authorize disclosure, including removed memberships.
            var result = await ReadAsync(organizationId, actorUserId, cancellationToken);
            if (result.Succeeded) items.Add(result.Value!);
            else if (result.ErrorCode != "organization_not_found")
                return OrganizationOperation<OrganizationDirectoryPage>.Failure(result.ErrorCode!);
        }
        if (!await actors.VerifyAsync(actorUserId, cancellationToken))
            return OrganizationOperation<OrganizationDirectoryPage>.Failure("session_unavailable");
        return OrganizationOperation<OrganizationDirectoryPage>.Success(new(items,
            routes.Count > 50 ? candidates[^1] : null));
    }

    public Task<OrganizationOperation<OrganizationSummary>> ReadAsync(Guid organizationId,
        Guid actorUserId, CancellationToken cancellationToken = default) =>
        organizationId == Guid.Empty
            ? Task.FromResult(OrganizationOperation<OrganizationSummary>.Failure("organization_not_found")) :
        unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false, async () =>
        {
            var membership = await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken);
            if (membership?.Active != true)
                return OrganizationOperation<OrganizationSummary>.Failure("organization_not_found");
            var organization = await store.FindOrganizationAsync(organizationId, cancellationToken);
            if (organization?.Status != OrganizationStatus.Active)
                return OrganizationOperation<OrganizationSummary>.Failure("organization_not_found");
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return OrganizationOperation<OrganizationSummary>.Failure("session_unavailable");
            return OrganizationOperation<OrganizationSummary>.Success(new(organization, membership.Role));
        }, cancellationToken);

    // ARCH-02-AC-003: admission is a current read, never a transferable grant.
    public Task<OrganizationOperation<OrganizationSurfaceAdmission>> ReadSurfaceAdmissionAsync(Guid organizationId,
        Guid actorUserId, bool portal, CancellationToken cancellationToken = default) =>
        organizationId == Guid.Empty
            ? Task.FromResult(OrganizationOperation<OrganizationSurfaceAdmission>.Failure("organization_not_found")) :
        unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false, async () =>
        {
            var admitted = portal
                ? await invitations.HasActivePortalAccessAsync(organizationId, actorUserId, cancellationToken)
                : (await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken))?.Active == true;
            if (!admitted) return OrganizationOperation<OrganizationSurfaceAdmission>.Failure("organization_not_found");
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return OrganizationOperation<OrganizationSurfaceAdmission>.Failure("session_unavailable");
            return OrganizationOperation<OrganizationSurfaceAdmission>.Success(new(organizationId, portal ? "PORTAL" : "INTERNAL"));
        }, cancellationToken);
    public Task<OrganizationOperation<OrganizationMemberReview>> ReviewMemberAsync(Guid organizationId,
        Guid actorUserId, Guid targetUserId, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(organizationId, actorUserId, targetUserId, false, async () =>
        {
            var actor = await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken);
            if (!CanAdminister(actor))
                return OrganizationOperation<OrganizationMemberReview>.Failure("organization_not_found");
            var rows = await store.ListActiveMembersAsync(organizationId, null, cancellationToken, targetUserId);
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return OrganizationOperation<OrganizationMemberReview>.Failure("session_unavailable");
            return OrganizationOperation<OrganizationMemberReview>.Success(new(organizationId, rows.SingleOrDefault(), actor!.Role));
        }, cancellationToken);

    public Task<OrganizationOperation<OrganizationMemberPage>> ListMembersAsync(Guid organizationId,
        Guid actorUserId, Guid? after, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false, async () =>
        {
            // Admission and reads share the existing parent/member/account/session
            // transaction; a demotion or revocation committed during a wait wins.
            var actor = await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken);
            if (!CanAdminister(actor))
                return OrganizationOperation<OrganizationMemberPage>.Failure("organization_not_found");
            if (after == Guid.Empty) return OrganizationOperation<OrganizationMemberPage>.Failure("invalid_member_cursor");
            var rows = await store.ListActiveMembersAsync(organizationId, after, cancellationToken);
            if (!await actors.VerifyAsync(actorUserId, cancellationToken))
                return OrganizationOperation<OrganizationMemberPage>.Failure("session_unavailable");
            var items = rows.Take(50).ToArray();
            return OrganizationOperation<OrganizationMemberPage>.Success(new(organizationId, items,
                rows.Count > 50 ? items[^1].UserId : null, actor!.Role));
        }, cancellationToken);

    public Task<OrganizationOperation<OrganizationSummary>> CreateAsync(
        Guid actorUserId, string name, string? description, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationId = Guid.NewGuid();
        return unitOfWork.ExecuteAsync(organizationId, actorUserId, null, true,
            () => CreateCoreAsync(organizationId, actorUserId, name, description, correlationId, cancellationToken), cancellationToken);
    }

    public Task<OrganizationOperation<OrganizationRecord>> UpdateAsync(
        Guid organizationId, Guid actorUserId, string name, string? description, string? logoUrl,
        long expectedVersion, string correlationId, CancellationToken cancellationToken = default, Guid? idempotencyKey = null) =>
        unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false,
            async () =>
            {
                if (idempotencyKey is null)
                    return await UpdateCoreAsync(organizationId, actorUserId, name, description, logoUrl, expectedVersion, correlationId, cancellationToken);
                if (!CanAdminister(await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken)))
                    return OrganizationOperation<OrganizationRecord>.Failure("organization_not_found");
                if (idempotencyKey == Guid.Empty)
                    return OrganizationOperation<OrganizationRecord>.Failure("invalid_idempotency_key");
                var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { organizationId, name, description, logoUrl, expectedVersion })));
                var replay = await metadataReplays.ReadAsync(organizationId, actorUserId, idempotencyKey.Value, cancellationToken);
                if (replay is not null)
                {
                    if (replay.Fingerprint != fingerprint)
                        return OrganizationOperation<OrganizationRecord>.Failure("idempotency_conflict");
                    if (replay.ExpiresAt <= clock.UtcNow)
                        return OrganizationOperation<OrganizationRecord>.Failure("idempotency_expired");
                    return OrganizationOperation<OrganizationRecord>.Success(replay.Result);
                }
                var result = await UpdateCoreAsync(organizationId, actorUserId, name, description, logoUrl, expectedVersion, correlationId, cancellationToken);
                if (result.Succeeded && result.Value is not null)
                    await metadataReplays.SaveAsync(organizationId, actorUserId, idempotencyKey.Value,
                        new(fingerprint, result.Value, clock.UtcNow.AddHours(24)), cancellationToken);
                return result;
            }, cancellationToken);

    public Task<OrganizationOperation<bool>> RemoveMemberAsync(
        Guid organizationId, Guid actorUserId, Guid targetUserId, string correlationId,
        CancellationToken cancellationToken = default, long? expectedVersion = null, Guid? idempotencyKey = null) =>
        unitOfWork.ExecuteAsync(organizationId, actorUserId, targetUserId, false, async () =>
        {
            if (idempotencyKey is null)
                return await RemoveMemberCoreAsync(organizationId, actorUserId, targetUserId, correlationId, cancellationToken, expectedVersion);
            // Current administrators can recover a token-free acknowledgment; self-removal
            // deliberately retires the actor's membership and uses the current session instead.
            if (actorUserId != targetUserId && !CanAdminister(await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken)))
                return OrganizationOperation<bool>.Failure("organization_not_found");
            if (idempotencyKey == Guid.Empty) return OrganizationOperation<bool>.Failure("invalid_idempotency_key");
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { organizationId, targetUserId, expectedVersion })));
            var receipt = await removalReplays.ReadAsync(organizationId, actorUserId, idempotencyKey.Value, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.Fingerprint != fingerprint) return OrganizationOperation<bool>.Failure("idempotency_conflict");
                return receipt.ExpiresAt > clock.UtcNow ? OrganizationOperation<bool>.Success(true)
                    : OrganizationOperation<bool>.Failure("idempotency_expired");
            }
            var result = await RemoveMemberCoreAsync(organizationId, actorUserId, targetUserId, correlationId, cancellationToken, expectedVersion);
            if (result.Succeeded) await removalReplays.SaveAsync(organizationId, actorUserId, idempotencyKey.Value,
                new(fingerprint, clock.UtcNow.AddHours(24)), cancellationToken);
            return result;
        }, cancellationToken);

    public Task<OrganizationOperation<bool>> LeaveAsync(
        Guid organizationId, Guid actorUserId, string correlationId,
        CancellationToken cancellationToken = default, Guid? idempotencyKey = null) =>
        unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false,
            async () =>
            {
                if (idempotencyKey is null) return await LeaveCoreAsync(organizationId, actorUserId, correlationId, cancellationToken);
                if (idempotencyKey == Guid.Empty) return OrganizationOperation<bool>.Failure("invalid_idempotency_key");
                // A committed departure deliberately retires membership. Only
                // the same current account/session can read its token-free receipt.
                var receipt = await departureReplays.ReadAsync(organizationId, actorUserId, idempotencyKey.Value, cancellationToken);
                if (receipt is not null) return receipt.ExpiresAt > clock.UtcNow
                    ? OrganizationOperation<bool>.Success(true) : OrganizationOperation<bool>.Failure("idempotency_expired");
                var result = await LeaveCoreAsync(organizationId, actorUserId, correlationId, cancellationToken);
                if (result.Succeeded) await departureReplays.SaveAsync(organizationId, actorUserId, idempotencyKey.Value,
                    new(clock.UtcNow.AddHours(24)), cancellationToken);
                return result;
            }, cancellationToken);

    public Task<OrganizationOperation<bool>> MarkDeletingAsync(
        Guid organizationId, Guid actorUserId, long expectedVersion, string correlationId,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false,
            () => MarkDeletingCoreAsync(organizationId, actorUserId, expectedVersion, correlationId, cancellationToken), cancellationToken);

    private async Task<OrganizationOperation<OrganizationSummary>> CreateCoreAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 160)
        {
            return OrganizationOperation<OrganizationSummary>.Failure(
                "invalid_organization_name");
        }

        var organization = await store.CreateOrganizationAsync(
            actorUserId,
            organizationId,
            normalizedName,
            NormalizeOptional(description),
            clock.UtcNow,
            cancellationToken);

        await store.AppendAuditAsync(
            organization.Id,
            actorUserId,
            "ORGANIZATION_CREATED",
            "Organization",
            organization.Id,
            correlationId,
            cancellationToken);

        return OrganizationOperation<OrganizationSummary>.Success(
            new OrganizationSummary(organization, OrganizationRole.Owner));
    }

    public Task<IReadOnlyList<OrganizationSummary>> ListAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        store.ListOrganizationsForUserAsync(actorUserId, cancellationToken);

    private async Task<OrganizationOperation<OrganizationRecord>> UpdateCoreAsync(
        Guid organizationId,
        Guid actorUserId,
        string name,
        string? description,
        string? logoUrl,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (!CanAdminister(membership))
        {
            return OrganizationOperation<OrganizationRecord>.Failure(
                "organization_not_found");
        }

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 160)
        {
            return OrganizationOperation<OrganizationRecord>.Failure(
                "invalid_organization_name");
        }

        var cleanLogoUrl = NormalizeOptional(logoUrl);
        if (cleanLogoUrl is not null && (cleanLogoUrl.Length > 2048 ||
            !Uri.TryCreate(cleanLogoUrl, UriKind.Absolute, out var logoUri) ||
            logoUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(logoUri.UserInfo)))
            return OrganizationOperation<OrganizationRecord>.Failure("invalid_organization_logo_url");

        var updated = await store.UpdateOrganizationAsync(
            organizationId,
            normalizedName,
            NormalizeOptional(description),
            cleanLogoUrl,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return OrganizationOperation<OrganizationRecord>.Failure(
                "version_conflict");
        }

        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_UPDATED",
            "Organization",
            organizationId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<OrganizationRecord>.Success(updated);
    }

    public Task<OrganizationOperation<OrganizationBoardDirectoryPage>> ListBoardsPageAsync(
        Guid organizationId, Guid actorUserId, Guid? after, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
            return Task.FromResult(OrganizationOperation<OrganizationBoardDirectoryPage>.Failure("organization_not_found"));
        if (after == Guid.Empty)
            return Task.FromResult(OrganizationOperation<OrganizationBoardDirectoryPage>.Failure("invalid_board_directory_cursor"));
        return unitOfWork.ExecuteAsync(organizationId, actorUserId, null, false, async () =>
        {
            var member = await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken);
            if (member is not { Active: true })
                return OrganizationOperation<OrganizationBoardDirectoryPage>.Failure("organization_not_found");
            var rows = await workStore.ListActiveVisibleBoardsPageAsync(organizationId, actorUserId,
                member.Role is OrganizationRole.Owner or OrganizationRole.Admin, after, cancellationToken);
            // Refuse the whole page if admission changed during the read. The
            // owning production transaction holds the parent/member locks.
            var current = await store.FindMembershipAsync(organizationId, actorUserId, cancellationToken);
            if (current is not { Active: true } || current.Role != member.Role
                || (await store.FindOrganizationAsync(organizationId, cancellationToken))?.Status != OrganizationStatus.Active)
                return OrganizationOperation<OrganizationBoardDirectoryPage>.Failure("organization_not_found");
            var items = rows.Take(50).ToArray();
            return OrganizationOperation<OrganizationBoardDirectoryPage>.Success(new(organizationId, items,
                rows.Count > 50 ? items[^1].Id : null));
        }, cancellationToken);
    }

    public async Task<OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>> ListBoardsAsync(
        Guid organizationId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null || !membership.Active)
        {
            return OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>.Failure(
                "organization_not_found");
        }

        var boards = await workStore.ListVisibleBoardsAsync(
            organizationId,
            actorUserId,
            membership.Role is OrganizationRole.Owner or OrganizationRole.Admin,
            cancellationToken);

        return OrganizationOperation<IReadOnlyList<OrganizationBoardSummary>>.Success(
            boards);
    }

    private async Task<OrganizationOperation<bool>> RemoveMemberCoreAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid targetUserId,
        string correlationId,
        CancellationToken cancellationToken = default,
        long? expectedVersion = null)
    {
        var actor = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (!CanAdminister(actor))
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        var target = await store.FindMembershipAsync(
            organizationId,
            targetUserId,
            cancellationToken);

        if (target is null || !target.Active)
        {
            return OrganizationOperation<bool>.Failure("member_not_found");
        }

        if (actor!.Role == OrganizationRole.Admin &&
            target.Role == OrganizationRole.Owner)
        {
            return OrganizationOperation<bool>.Failure("insufficient_permission");
        }

        // The unit of work holds the parent and both membership locks. Check
        // the freshly authorized target before changing membership or audit.
        if (expectedVersion is <= 0)
            return OrganizationOperation<bool>.Failure("invalid_member_version");
        if (expectedVersion is not null && expectedVersion != target.Version)
            return OrganizationOperation<bool>.Failure("member_version_conflict");

        var result = await store.RemoveMemberAsync(
            organizationId,
            targetUserId,
            clock.UtcNow,
            cancellationToken);

        if (result == OrganizationRemoveMemberResult.SoleOwner)
        {
            return OrganizationOperation<bool>.Failure("sole_owner");
        }

        if (result == OrganizationRemoveMemberResult.NotFound)
        {
            return OrganizationOperation<bool>.Failure("member_not_found");
        }

        await RemoveAssignmentsAsync(organizationId, targetUserId, actorUserId, correlationId, cancellationToken);
        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_MEMBER_REMOVED",
            "User",
            targetUserId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<bool>.Success(true);
    }

    private async Task<OrganizationOperation<bool>> LeaveCoreAsync(
        Guid organizationId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null || !membership.Active)
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        var result = await store.RemoveMemberAsync(
            organizationId,
            actorUserId,
            clock.UtcNow,
            cancellationToken);

        if (result == OrganizationRemoveMemberResult.SoleOwner)
        {
            return OrganizationOperation<bool>.Failure("sole_owner");
        }

        if (result != OrganizationRemoveMemberResult.Removed)
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        await RemoveAssignmentsAsync(organizationId, actorUserId, actorUserId, correlationId, cancellationToken);
        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_MEMBER_LEFT",
            "User",
            actorUserId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<bool>.Success(true);
    }

    private async Task RemoveAssignmentsAsync(Guid organizationId, Guid userId, Guid actorUserId,
        string correlationId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var cards = await workStore.RemoveOrganizationCardMemberAssignmentsAsync(organizationId, userId, now, cancellationToken);
        foreach (var card in cards)
        {
            await workStore.AppendAuditAsync(organizationId, actorUserId, "CARD_MEMBER_REMOVED", "Card", card.Id, correlationId, cancellationToken);
            await workEvents.AppendAsync(new WorkEvent(Guid.NewGuid(), organizationId, card.BoardId, actorUserId,
                "CARD_MEMBER_REMOVED", "Card", card.Id, card.Version, correlationId, now), cancellationToken);
        }
    }

    private async Task<OrganizationOperation<bool>> MarkDeletingCoreAsync(
        Guid organizationId,
        Guid actorUserId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await store.FindMembershipAsync(
            organizationId,
            actorUserId,
            cancellationToken);

        if (membership is null ||
            !membership.Active ||
            membership.Role != OrganizationRole.Owner)
        {
            return OrganizationOperation<bool>.Failure("organization_not_found");
        }

        // The Organization command owns the parent gate. Lock every chosen
        // Board in stable order before making the parent unavailable; never use
        // a paged UI Board directory to truncate lifecycle effects.
        var reminderBoards = await workStore.ListReminderCandidateBoardIdsAsync(organizationId, cancellationToken);
        foreach (var boardId in reminderBoards)
            if (!await workStore.AcquireCommandScopeAsync(organizationId, actorUserId, boardId, cancellationToken))
                return OrganizationOperation<bool>.Failure("organization_not_found");
        var changed = await store.MarkDeletingAsync(
            organizationId,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (!changed)
        {
            return OrganizationOperation<bool>.Failure("version_conflict");
        }

        foreach (var boardId in reminderBoards)
            await reminders.RescheduleAsync(organizationId, boardId, null, actorUserId, correlationId, cancellationToken);

        await store.AppendAuditAsync(
            organizationId,
            actorUserId,
            "ORGANIZATION_DELETION_REQUESTED",
            "Organization",
            organizationId,
            correlationId,
            cancellationToken);

        return OrganizationOperation<bool>.Success(true);
    }

    private static bool CanAdminister(OrganizationMembership? membership) =>
        membership is
        {
            Active: true,
            Role: OrganizationRole.Owner or OrganizationRole.Admin,
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
