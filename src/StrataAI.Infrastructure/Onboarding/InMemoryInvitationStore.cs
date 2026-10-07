using StrataAI.Application.Onboarding;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed class InMemoryInvitationStore(
    IOrganizationStore organizationStore, IClock clock, IWorkManagementStore work, InMemoryInvitationRecipientJournal journal) : IInvitationStore, IInvitationHistoryStore,
    StrataAI.Infrastructure.Organizations.IDemoOrganizationTransactionParticipant
{
    private readonly object _sync = new();
    private readonly Dictionary<string, InvitationRecord> _byToken =
        new(StringComparer.Ordinal);
    private readonly HashSet<(Guid OrganizationId, Guid UserId, string Relationship)> _portalAccess = [];
    private readonly Dictionary<(Guid Organization, Guid Invitation), long> _versions = [];
    private readonly Dictionary<Guid, SortedSet<(DateTimeOffset At, Guid Id)>> _authorityIndex = [];
    private readonly Dictionary<Guid, string> _tokensById = [];
    internal Guid[] ReadIssuerAuthorityOrganizations(Guid issuer, DateTimeOffset cutoff, Guid after, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) return _byToken.Values.Where(row => row.CreatedByUserId == issuer && row.CreatedAt <= cutoff)
            .Select(row => row.OrganizationId).Distinct().Where(id => id.CompareTo(after) > 0).Order().Take(100).ToArray();
    }
    internal InvitationRecord[] ReadAuthorityPage(Guid organization, DateTimeOffset cutoff,
        (DateTimeOffset At, Guid Id) after, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_authorityIndex.TryGetValue(organization, out var index) || cutoff < after.At) return [];
            var maximum = (cutoff, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            return index.GetViewBetween(after, maximum).Where(key => key.CompareTo(after) > 0)
                .Take(100).Select(key => _byToken[_tokensById[key.Id]]).ToArray();
        }
    }
    internal bool HasPortalRelationship(Guid organization, Guid user, string role)
    { lock (_sync) return _portalAccess.Contains((organization, user, role)); }
    public Action CaptureRollback()
    {
        lock (_sync)
        {
            var invitations = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_byToken);
            var receipts = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_creationReplays);
            var portal = StrataAI.Infrastructure.WorkManagement.DemoRollback.Set(_portalAccess);
            var versions = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_versions);
            var tokens = StrataAI.Infrastructure.WorkManagement.DemoRollback.Dictionary(_tokensById);
            var index = _authorityIndex.ToDictionary(row => row.Key, row => new SortedSet<(DateTimeOffset At, Guid Id)>(row.Value));
            return () => { lock (_sync) { invitations(); receipts(); portal(); versions(); tokens();
                _authorityIndex.Clear(); foreach (var row in index) _authorityIndex.Add(row.Key, row.Value); } };
        }
    }
    public Task<bool> HasActivePortalAccessAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(_portalAccess.Any(row => row.OrganizationId == organizationId && row.UserId == userId));
    }
    private readonly Dictionary<(Guid OrganizationId, Guid ActorId, Guid Key), (string Fingerprint, Guid InvitationId, DateTimeOffset ExpiresAt)> _creationReplays = [];

    public Task<IReadOnlyList<IssuedInvitation>> ListAsync(Guid organizationId, Guid? after, CancellationToken cancellationToken, Guid? boardId = null)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<IssuedInvitation>>(_byToken.Values
                .Where(row => row.BoardTarget?.BoardId == boardId && row.OrganizationId == organizationId && (after is null || row.Id.CompareTo(after.Value) > 0))
                .OrderBy(row => row.Id).Take(51).Select(row => new IssuedInvitation(row.Id, row.InvitedEmail, row.Surface,
                    row.TargetRole, row.CreatedAt, row.ExpiresAt, row.AcceptedAt, row.RevokedAt, null, row.BoardTarget)).ToArray());
    }

    public Task<InvitationCreationReplay?> FindCreationReplayAsync(Guid organizationId, Guid actorId, Guid key,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_creationReplays.TryGetValue((organizationId, actorId, key), out var receipt))
                return Task.FromResult<InvitationCreationReplay?>(null);
            var invitation = _byToken.Values.Single(row => row.Id == receipt.InvitationId && row.OrganizationId == organizationId);
            return Task.FromResult<InvitationCreationReplay?>(new(receipt.Fingerprint, receipt.ExpiresAt <= clock.UtcNow, invitation));
        }
    }
    public Task SaveCreationReplayAsync(Guid organizationId, Guid actorId, Guid key, string fingerprint,
        Guid invitationId, CancellationToken cancellationToken = default)
    {
        lock (_sync) _creationReplays.Add((organizationId, actorId, key), (fingerprint, invitationId, clock.UtcNow.AddHours(24)));
        return Task.CompletedTask;
    }

    public Task<InvitationRecord> CreateAsync(
        InvitationRecord invitation,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _byToken[invitation.TokenHash] = invitation;
            _versions.Add((invitation.OrganizationId, invitation.Id), 1);
            _tokensById.Add(invitation.Id, invitation.TokenHash);
            if (!_authorityIndex.TryGetValue(invitation.OrganizationId, out var index))
                _authorityIndex.Add(invitation.OrganizationId, index = []);
            index.Add((invitation.CreatedAt, invitation.Id));
            journal.Capture(invitation, 1, "INVITATION_CREATED", cancellationToken);
        }

        return Task.FromResult(invitation);
    }

    public Task<IReadOnlyList<PendingInvitation>> ListPendingForEmailAsync(
        string emailNormalized,
        DateTimeOffset now,
        Guid? after,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var result = _byToken.Values
                .Where(
                    invitation =>
                        invitation.EmailNormalized == emailNormalized &&
                        invitation.AcceptedAt is null &&
                        invitation.RevokedAt is null &&
                        invitation.ExpiresAt > now && (after is null || invitation.Id.CompareTo(after.Value) > 0))
                .OrderBy(invitation => invitation.Id)
                .Take(51)
                .Select(
                    invitation =>
                        new PendingInvitation(
                            invitation.Id,
                            invitation.OrganizationId,
                            invitation.Surface,
                            invitation.TargetRole,
                            invitation.ExpiresAt, invitation.OrganizationName ?? "Organization", invitation.BoardTarget))
                .ToArray();

            return Task.FromResult<IReadOnlyList<PendingInvitation>>(result);
        }
    }

    public Task<InvitationRecord?> FindActiveByIdForEmailAsync(Guid invitationId, Guid actorUserId, string emailNormalized,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_byToken.Values.FirstOrDefault(i => i.Id == invitationId && i.EmailNormalized == emailNormalized
                && (i.AcceptedAt is null || i.AcceptedByUserId == actorUserId) && i.RevokedAt is null && i.ExpiresAt > clock.UtcNow));
        }
    }

    public Task<InvitationRecord?> FindActiveByTokenHashAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_byToken.TryGetValue(tokenHash, out var invitation) ||
                invitation.AcceptedAt is not null ||
                invitation.RevokedAt is not null ||
                invitation.ExpiresAt <= now)
            {
                return Task.FromResult<InvitationRecord?>(null);
            }

            return Task.FromResult<InvitationRecord?>(invitation);
        }
    }

    public async Task<InvitationAcceptStoreResult> AcceptAsync(
        string tokenHash,
        Guid userId,
        string emailNormalized,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default)
    {
        InvitationRecord invitation;
        var candidate = await FindActiveByTokenHashAsync(tokenHash, clock.UtcNow, cancellationToken);
        if (candidate?.BoardTarget is { } target)
        {
            var board = await work.FindBoardAsync(target.BoardId, cancellationToken);
            if (board is not { LifecycleState: BoardLifecycleState.Active } || board.OrganizationId != candidate.OrganizationId
                || candidate.Surface != InvitationSurface.Internal || candidate.TargetRole != "MEMBER" || !Enum.IsDefined(target.Role))
                return new(false, "invalid_or_expired_invitation", null);
        }

        lock (_sync)
        {
            if (!_byToken.TryGetValue(tokenHash, out invitation!) ||
                invitation.AcceptedAt is not null ||
                invitation.RevokedAt is not null ||
                invitation.ExpiresAt <= clock.UtcNow ||
                invitation.EmailNormalized != emailNormalized)
            {
                return new InvitationAcceptStoreResult(
                    false,
                    "invalid_or_expired_invitation",
                    null);
            }

            invitation = invitation with { AcceptedAt = acceptedAt, AcceptedByUserId = userId };
            _byToken[tokenHash] = invitation;
            _versions[(invitation.OrganizationId, invitation.Id)]++;
        }

        var existingMembership = invitation.BoardTarget is not null
            ? await organizationStore.FindMembershipAsync(invitation.OrganizationId, userId, cancellationToken) : null;
        if (invitation.Surface == InvitationSurface.Internal)
        {
            await organizationStore.AddOrRestoreMemberAsync(
                invitation.OrganizationId,
                userId,
                existingMembership is { Active: true } ? existingMembership.Role : ParseRole(invitation.TargetRole),
                acceptedAt,
                cancellationToken);
        }
        else
        {
            lock (_sync)
            {
                _portalAccess.Add(
                    (
                        invitation.OrganizationId,
                        userId,
                        invitation.TargetRole
                    ));
            }
        }

        if (invitation.BoardTarget is { } boardTarget)
        {
            var existingBoard = await work.FindBoardMemberAsync(boardTarget.BoardId, userId, cancellationToken);
            var role = existingMembership is { Active: true } && existingBoard is { Active: true, Role: BoardRole.Admin }
                ? BoardRole.Admin : boardTarget.Role;
            await work.UpsertBoardMemberAsync(boardTarget.BoardId, userId, role, acceptedAt, cancellationToken);
        }
        lock (_sync) journal.Capture(invitation, _versions[(invitation.OrganizationId, invitation.Id)], "INVITATION_ACCEPTED", cancellationToken);
        return new InvitationAcceptStoreResult(true, null, invitation);
    }

    public Task<InvitationRecord?> FindByIdAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult(_byToken.Values.FirstOrDefault(row => row.OrganizationId == organizationId && row.Id == invitationId));
    }

    public Task<bool> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default, Guid? boardId = null)
    {
        lock (_sync)
        {
            var pair = _byToken.FirstOrDefault(
                item =>
                    item.Value.OrganizationId == organizationId &&
                    item.Value.Id == invitationId && (boardId is null || item.Value.BoardTarget?.BoardId == boardId) &&
                    item.Value.AcceptedAt is null &&
                    item.Value.RevokedAt is null);

            if (string.IsNullOrEmpty(pair.Key))
            {
                return Task.FromResult(false);
            }

            _byToken[pair.Key] = pair.Value with { RevokedAt = revokedAt };
            var version = ++_versions[(organizationId, invitationId)];
            journal.Capture(_byToken[pair.Key], version, "INVITATION_REVOKED", cancellationToken);
            return Task.FromResult(true);
        }
    }

    private static OrganizationRole ParseRole(string value) =>
        value switch
        {
            "OWNER" => OrganizationRole.Owner,
            "ADMIN" => OrganizationRole.Admin,
            _ => OrganizationRole.Member,
        };
}
