namespace StrataAI.Application.Onboarding;

public interface IInvitationStore
{
    Task CreateAsync(
        InvitationRecord invitation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingInvitation>> ListPendingForEmailAsync(
        string emailNormalized,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

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

    Task<bool> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);
}

public sealed record InvitationAcceptStoreResult(
    bool Succeeded,
    string? ErrorCode,
    InvitationRecord? Invitation);
