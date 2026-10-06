using System.Security.Cryptography;
using System.Text.Json;

namespace StrataAI.Application.Identity;

public sealed class TransactionalIdentityService(IIdentityService inner, IIdentityUnitOfWork commands,
    IIdentityCommandContext context, IIdentityProfileReplayStore profileReplays, ISecureTokenService tokens,
    ICommandActorAuthorization actors) : IIdentityService
{
    public Task<IdentityOperation<IdentitySyncSnapshot>> ReadEventsAsync(Guid userId, long? after,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteAsync(userId, () => inner.ReadEventsAsync(userId, after, cancellationToken), cancellationToken);
    public Task<IdentityOperation<RegistrationOutcome>> RegisterAsync(
        string email,
        string password,
        string displayName,
        string? locale,
        string? timezone,
        string correlationId,
        CancellationToken cancellationToken = default, string? invitationToken = null) =>
        commands.ExecuteRegistrationAsync(() => inner.RegisterAsync(email, password, displayName, locale, timezone, correlationId, cancellationToken, invitationToken), cancellationToken);

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
        context.IdempotencyKey is Guid key
            ? context.RevocationSessionTokenHash is string hash && hash == tokens.Hash(rawSessionToken)
                ? commands.ExecuteRevocationAsync(actorId, hash, key, IdentityRevocationKind.Logout, correlationId,
                    actor => inner.LogoutAsync(rawSessionToken, actor, correlationId, cancellationToken), cancellationToken)
                : Task.FromResult(IdentityOperation<bool>.Failure("session_unavailable"))
            : commands.ExecuteAsync(actorId, () => inner.LogoutAsync(rawSessionToken, actorId, correlationId, cancellationToken), cancellationToken);

    public Task<PasswordResetRequestOutcome> RequestPasswordResetAsync(
        string email,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteRecoveryRequestAsync(() => inner.RequestPasswordResetAsync(email, correlationId, cancellationToken), new PasswordResetRequestOutcome(null), cancellationToken);

    public Task<string?> RequestEmailVerificationAsync(
        string email,string correlationId,CancellationToken cancellationToken=default) =>
        commands.ExecuteRecoveryRequestAsync<string?>(() => inner.RequestEmailVerificationAsync(email, correlationId, cancellationToken), null, cancellationToken);

    public Task<IdentityOperation<UserProfile>> ResetPasswordAsync(
        string rawResetToken,
        string newPassword,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteTokenProofAsync(() => inner.ResetPasswordAsync(rawResetToken, newPassword, correlationId, cancellationToken), cancellationToken);

    public Task<IdentityOperation<UserProfile>> VerifyEmailAsync(
        string rawVerificationToken,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteTokenProofAsync(() => inner.VerifyEmailAsync(rawVerificationToken, correlationId, cancellationToken), cancellationToken);

    public Task<IdentityOperation<UserProfile>> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? avatarUrl,
        string? locale,
        string? timezone,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        commands.ExecuteAsync(userId, async () =>
        {
            // Authorization and the subject lock precede any stored acknowledgment disclosure.
            var key = context.IdempotencyKey;
            if (key is null)
                return await AdmitProfile(await inner.UpdateProfileAsync(userId, displayName, avatarUrl, locale, timezone, expectedVersion, correlationId, cancellationToken));
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { Operation = "PROFILE_UPDATE", displayName, avatarUrl, locale, timezone, expectedVersion })));
            var prior = await profileReplays.ReadAsync(userId, key.Value, cancellationToken);
            if (prior is not null)
                return prior.Fingerprint == fingerprint ? await AdmitProfile(IdentityOperation<UserProfile>.Success(prior.Profile))
                    : IdentityOperation<UserProfile>.Failure("idempotency_key_reused");
            var result = await inner.UpdateProfileAsync(userId, displayName, avatarUrl, locale, timezone, expectedVersion, correlationId, cancellationToken);
            if (result.Succeeded && result.Value is not null)
                await profileReplays.SaveAsync(userId, key.Value, new IdentityProfileReplay(fingerprint, result.Value), cancellationToken);
            return await AdmitProfile(result);

            async Task<IdentityOperation<UserProfile>> AdmitProfile(IdentityOperation<UserProfile> outcome) =>
                outcome.Succeeded && !await actors.VerifyAsync(userId, cancellationToken)
                    ? IdentityOperation<UserProfile>.Failure("session_unavailable") : outcome;
        }, cancellationToken);

    public Task<IdentityOperation<bool>> DeactivateAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        context.IdempotencyKey is Guid key
            ? context.RevocationSessionTokenHash is string hash
                ? commands.ExecuteRevocationAsync(userId, hash, key, IdentityRevocationKind.Deactivate, correlationId,
                    actor => inner.DeactivateAsync(actor, correlationId, cancellationToken), cancellationToken)
                : Task.FromResult(IdentityOperation<bool>.Failure("session_unavailable"))
            : commands.ExecuteDeactivationAsync(userId, () => inner.DeactivateAsync(userId, correlationId, cancellationToken), cancellationToken, correlationId);

}
