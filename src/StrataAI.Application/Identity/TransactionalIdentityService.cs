namespace StrataAI.Application.Identity;

public sealed class TransactionalIdentityService(IIdentityService inner, IIdentityUnitOfWork commands) : IIdentityService
{
    public Task<IdentityOperation<RegistrationOutcome>> RegisterAsync(
        string email,
        string password,
        string displayName,
        string? locale,
        string? timezone,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteRegistrationAsync(() => inner.RegisterAsync(email, password, displayName, locale, timezone, correlationId, cancellationToken), cancellationToken);

    public Task<IdentityOperation<LoginOutcome>> LoginAsync(
        string email,
        string password,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteSignInAsync(() => inner.LoginAsync(email, password, correlationId, cancellationToken), cancellationToken);

    public Task<AuthenticatedSession?> AuthenticateSessionAsync(
        string rawSessionToken,
        CancellationToken cancellationToken = default) =>
        inner.AuthenticateSessionAsync(rawSessionToken, cancellationToken);

    public Task<IdentityOperation<bool>> LogoutAsync(
        string rawSessionToken,
        Guid actorId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteAsync(actorId, () => inner.LogoutAsync(rawSessionToken, actorId, correlationId, cancellationToken), cancellationToken);

    public Task<PasswordResetRequestOutcome> RequestPasswordResetAsync(
        string email,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        inner.RequestPasswordResetAsync(email, correlationId, cancellationToken);

    public Task<string?> RequestEmailVerificationAsync(
        string email,string correlationId,CancellationToken cancellationToken=default) =>
        inner.RequestEmailVerificationAsync(email, correlationId, cancellationToken);

    public Task<IdentityOperation<UserProfile>> ResetPasswordAsync(
        string rawResetToken,
        string newPassword,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        inner.ResetPasswordAsync(rawResetToken, newPassword, correlationId, cancellationToken);

    public Task<IdentityOperation<UserProfile>> VerifyEmailAsync(
        string rawVerificationToken,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        inner.VerifyEmailAsync(rawVerificationToken, correlationId, cancellationToken);

    public Task<IdentityOperation<UserProfile>> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? avatarUrl,
        string? locale,
        string? timezone,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteAsync(userId, () => inner.UpdateProfileAsync(userId, displayName, avatarUrl, locale, timezone, expectedVersion, correlationId, cancellationToken), cancellationToken);

    public Task<IdentityOperation<bool>> DeactivateAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteAsync(userId, () => inner.DeactivateAsync(userId, correlationId, cancellationToken), cancellationToken);

}
