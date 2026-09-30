namespace StrataAI.Application.Identity;

public interface IIdentityService
{
    Task<IdentityOperation<RegistrationOutcome>> RegisterAsync(
        string email,
        string password,
        string displayName,
        string? locale,
        string? timezone,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IdentityOperation<LoginOutcome>> LoginAsync(
        string email,
        string password,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedSession?> AuthenticateSessionAsync(
        string rawSessionToken,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        string rawSessionToken,
        Guid actorId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<PasswordResetRequestOutcome> RequestPasswordResetAsync(
        string email,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<string?> RequestEmailVerificationAsync(string email,string correlationId,CancellationToken cancellationToken=default);

    Task<IdentityOperation<UserProfile>> ResetPasswordAsync(
        string rawResetToken,
        string newPassword,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IdentityOperation<UserProfile>> VerifyEmailAsync(
        string rawVerificationToken,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IdentityOperation<UserProfile>> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? avatarUrl,
        string? locale,
        string? timezone,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
