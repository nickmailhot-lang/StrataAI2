namespace StrataAI.Application.Onboarding;

public interface IInvitationStore
{
    Task<bool> HasActivePortalAccessAsync(Guid organizationId, Guid userId,
        CancellationToken cancellationToken = default);
    Task<InvitationCreationReplay?> FindCreationReplayAsync(Guid organizationId, Guid actorId, Guid key,
        CancellationToken cancellationToken = default);
    Task SaveCreationReplayAsync(Guid organizationId, Guid actorId, Guid key, string fingerprint,
        Guid invitationId, CancellationToken cancellationToken = default);
    Task<InvitationRecord> CreateAsync(
        InvitationRecord invitation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingInvitation>> ListPendingForEmailAsync(
        string emailNormalized,
        DateTimeOffset now,
        Guid? after,
        CancellationToken cancellationToken = default);

    Task<InvitationRecord?> FindActiveByIdForEmailAsync(Guid invitationId, Guid actorUserId, string emailNormalized,
        DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<InvitationRecord?> FindActiveByTokenHashAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<InvitationAcceptStoreResult> AcceptAsync(
        string tokenHash,
        Guid userId,
        string emailNormalized,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default);

    // Canonical read inside the authorized Organization command; includes terminal invitations.
    Task<InvitationRecord?> FindByIdAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default, Guid? boardId = null);
}

public sealed record InvitationAcceptStoreResult(
    bool Succeeded,
    string? ErrorCode,
    InvitationRecord? Invitation);
