using StrataAI.Application.Onboarding;
using StrataAI.Application.Common;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed class InMemoryInvitationStore(
    IOrganizationStore organizationStore, IClock clock) : IInvitationStore, IInvitationHistoryStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, InvitationRecord> _byToken =
        new(StringComparer.Ordinal);
    private readonly HashSet<(Guid OrganizationId, Guid UserId, string Relationship)> _portalAccess = [];
    private readonly Dictionary<(Guid OrganizationId, Guid ActorId, Guid Key), (string Fingerprint, Guid InvitationId, DateTimeOffset ExpiresAt)> _creationReplays = [];

    public Task<IReadOnlyList<IssuedInvitation>> ListAsync(Guid organizationId, Guid? after, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<IssuedInvitation>>(_byToken.Values
                .Where(row => row.BoardTarget is null && row.OrganizationId == organizationId && (after is null || row.Id.CompareTo(after.Value) > 0))
                .OrderBy(row => row.Id).Take(51).Select(row => new IssuedInvitation(row.Id, row.InvitedEmail, row.Surface,
                    row.TargetRole, row.CreatedAt, row.ExpiresAt, row.AcceptedAt, row.RevokedAt, null)).ToArray());
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
                        invitation.BoardTarget is null && invitation.EmailNormalized == emailNormalized &&
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
                            invitation.ExpiresAt, invitation.OrganizationName ?? "Organization"))
                .ToArray();

            return Task.FromResult<IReadOnlyList<PendingInvitation>>(result);
        }
    }

    public Task<InvitationRecord?> FindActiveByIdForEmailAsync(Guid invitationId, Guid actorUserId, string emailNormalized,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_byToken.Values.FirstOrDefault(i => i.BoardTarget is null && i.Id == invitationId && i.EmailNormalized == emailNormalized
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
                invitation.BoardTarget is not null || invitation.AcceptedAt is not null ||
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

        lock (_sync)
        {
            if (!_byToken.TryGetValue(tokenHash, out invitation!) ||
                invitation.BoardTarget is not null || invitation.AcceptedAt is not null ||
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
        }

        if (invitation.Surface == InvitationSurface.Internal)
        {
            await organizationStore.AddOrRestoreMemberAsync(
                invitation.OrganizationId,
                userId,
                ParseRole(invitation.TargetRole),
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

        return new InvitationAcceptStoreResult(true, null, invitation);
    }

    public Task<bool> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var pair = _byToken.FirstOrDefault(
                item =>
                    item.Value.OrganizationId == organizationId &&
                    item.Value.Id == invitationId &&
                    item.Value.AcceptedAt is null &&
                    item.Value.RevokedAt is null);

            if (string.IsNullOrEmpty(pair.Key))
            {
                return Task.FromResult(false);
            }

            _byToken[pair.Key] = pair.Value with { RevokedAt = revokedAt };
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
