using StrataAI.Application.Onboarding;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed class InMemoryInvitationStore(
    IOrganizationStore organizationStore) : IInvitationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, InvitationRecord> _byToken =
        new(StringComparer.Ordinal);
    private readonly HashSet<(Guid OrganizationId, Guid UserId, string Relationship)> _portalAccess = [];

    public Task CreateAsync(
        InvitationRecord invitation,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _byToken[invitation.TokenHash] = invitation;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingInvitation>> ListPendingForEmailAsync(
        string emailNormalized,
        DateTimeOffset now,
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
                        invitation.ExpiresAt > now)
                .OrderBy(invitation => invitation.ExpiresAt)
                .Select(
                    invitation =>
                        new PendingInvitation(
                            invitation.Id,
                            invitation.OrganizationId,
                            invitation.Surface,
                            invitation.TargetRole,
                            invitation.ExpiresAt))
                .ToArray();

            return Task.FromResult<IReadOnlyList<PendingInvitation>>(result);
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

        lock (_sync)
        {
            if (!_byToken.TryGetValue(tokenHash, out invitation!) ||
                invitation.AcceptedAt is not null ||
                invitation.RevokedAt is not null ||
                invitation.ExpiresAt <= acceptedAt ||
                invitation.EmailNormalized != emailNormalized)
            {
                return new InvitationAcceptStoreResult(
                    false,
                    "invalid_or_expired_invitation",
                    null);
            }

            invitation = invitation with { AcceptedAt = acceptedAt };
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
