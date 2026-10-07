using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using StrataAI.Application.Common;

namespace StrataAI.Application.Identity;

public sealed class IdentityService(
    IIdentityStore store,
    IPasswordHashService passwordHashes,
    ISecureTokenService tokens,
    IClock clock,
    IdentityPolicy policy, IIdentityCommandContext? context = null,
    IIdentityLoginReplayStore? loginReplays = null, IIdentityLoginRetrySecrets? loginSecrets = null,
    IIdentityRegistrationReplayStore? registrationReplays = null, IIdentityRegistrationRetrySecrets? registrationSecrets = null,
    IIdentityDeliveryTokenSigner? deliverySigner = null,
    IIdentityRecoveryRequestReplayStore? recoveryReplays = null, IIdentityRecoveryRetrySecrets? recoverySecrets = null,
    IIdentityTokenConsumptionReplayStore? consumptionReplays = null, IIdentityTokenConsumptionRetrySecrets? consumptionSecrets = null,
    IInvitationRegistrationProofStore? invitationRegistrations = null) : IIdentityService
{
    // Unknown addresses still perform adaptive verification. This process-local
    // dummy credential is never persisted or used to admit an account/session.
    private readonly string _unknownAccountHash = passwordHashes.Hash(Guid.Empty, tokens.Generate());
    public async Task<IdentityOperation<UserProfile>> ReadProfileAsync(Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);
        return user is null || user.Status != AccountStatus.Active
            ? IdentityOperation<UserProfile>.Failure("session_unavailable")
            : IdentityOperation<UserProfile>.Success(ToProfile(user));
    }

    public async Task<IdentityOperation<IdentitySyncSnapshot>> ReadEventsAsync(Guid userId, long? after,
        CancellationToken cancellationToken = default)
    {
        if (after is < 0) return IdentityOperation<IdentitySyncSnapshot>.Failure("invalid_identity_cursor");
        var user = await store.FindUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
            return IdentityOperation<IdentitySyncSnapshot>.Failure("session_unavailable");
        var result = await store.ReadEventsAsync(userId, after, cancellationToken);
        if (!result.Succeeded || result.Value is null)
            return IdentityOperation<IdentitySyncSnapshot>.Failure(result.ErrorCode ?? "identity_storage_unavailable");
        var page = result.Value;
        return IdentityOperation<IdentitySyncSnapshot>.Success(new(ToProfile(user), page.Cursor,
            page.LatestSequence, page.HasMore, page.Events));
    }
    public async Task<IdentityOperation<RegistrationOutcome>> RegisterAsync(
        string email,
        string password,
        string displayName,
        string? locale,
        string? timezone,
        string correlationId,
        CancellationToken cancellationToken = default, string? invitationToken = null)
    {
        if (!policy.AllowSelfRegistration && invitationToken is null)
        {
            return IdentityOperation<RegistrationOutcome>.Failure(
                "self_registration_disabled");
        }

        var normalized = NormalizeEmail(email);
        if (normalized is null)
        {
            return IdentityOperation<RegistrationOutcome>.Failure("invalid_email");
        }

        if (!IsValidPassword(password))
        {
            return IdentityOperation<RegistrationOutcome>.Failure("invalid_password");
        }

        var cleanDisplayName = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(cleanDisplayName) || cleanDisplayName.Length > 120)
        {
            return IdentityOperation<RegistrationOutcome>.Failure("invalid_display_name");
        }

        var profileLocale = NormalizeLocale(locale);
        if (!TryNormalizeLocale(profileLocale, out profileLocale))
            return IdentityOperation<RegistrationOutcome>.Failure("invalid_locale");
        var profileTimezone = NormalizeTimezone(timezone);
        if (!TryNormalizeTimezone(profileTimezone, out profileTimezone))
            return IdentityOperation<RegistrationOutcome>.Failure("invalid_timezone");

        InvitationRegistrationProof? invitationProof = null;
        string? invitationHash = null;
        if (invitationToken is not null)
        {
            if (string.IsNullOrWhiteSpace(invitationToken) || invitationToken.Length > 512 || invitationRegistrations is null)
                return IdentityOperation<RegistrationOutcome>.Failure("invalid_or_expired_invitation");
            invitationHash = tokens.Hash(invitationToken);
            invitationProof = await invitationRegistrations.PrepareAsync(invitationHash, normalized, cancellationToken);
            if (invitationProof is null)
                return IdentityOperation<RegistrationOutcome>.Failure("invalid_or_expired_invitation");
        }

        var registrationKey = context?.IdempotencyKey;
        if (registrationKey is not null)
        {
            if (registrationReplays is null || registrationSecrets is null)
                return IdentityOperation<RegistrationOutcome>.Failure("identity_storage_unavailable");
            var existing = await store.FindUserByNormalizedEmailAsync(normalized, cancellationToken);
            if (existing is not null) return await AcknowledgeRegistrationAsync(existing);
        }
        var registrationKeyVersion = registrationKey is null ? null : registrationSecrets!.CurrentKeyVersion;
        var now = new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        var status = policy.RequireVerifiedEmail
            ? AccountStatus.PendingVerification
            : AccountStatus.Active;

        var user = new UserIdentity(
            userId,
            email.Trim(),
            normalized,
            cleanDisplayName,
            null,
            profileLocale,
            profileTimezone,
            status,
            !policy.RequireVerifiedEmail,
            passwordHashes.Hash(userId, password),
            now,
            now,
            1);

        string? verificationToken = null;
        SecurityTokenRecord? verificationRecord = null;
        IdentityTokenDelivery? verificationDelivery = null;
        var verificationSource = RegistrationVerificationSource.None;
        string? verificationKeyVersion = null;
        if (policy.RequireVerifiedEmail)
        {
            var tokenId = Guid.NewGuid();
            var generated = tokens.GenerateForDelivery(tokenId, IdentityTokenPurpose.VerifyEmail, correlationId);
            verificationToken = generated.RawToken;
            verificationDelivery = generated.Delivery;
            if (registrationKey is not null)
            {
                if (verificationDelivery is not null)
                {
                    verificationSource = RegistrationVerificationSource.EmailDelivery;
                    verificationKeyVersion = verificationDelivery.KeyId;
                }
                else
                {
                    verificationSource = RegistrationVerificationSource.ApiRegistration;
                    verificationKeyVersion = registrationKeyVersion;
                    if (!registrationSecrets!.TryDeriveVerification(userId, tokenId, verificationKeyVersion!, out verificationToken))
                        return IdentityOperation<RegistrationOutcome>.Failure("identity_retry_key_unavailable");
                }
            }
            verificationRecord = new SecurityTokenRecord(
                    tokenId,
                    userId,
                    tokens.Hash(verificationToken),
                    now,
                    now.Add(policy.SecurityTokenLifetime));
        }
        if (!await store.TryCreateUserAsync(user, verificationRecord, verificationDelivery, cancellationToken))
        {
            if (registrationKey is not null)
            {
                // A concurrent insert may have committed the same intent while this INSERT waited.
                var winner = await store.FindUserByNormalizedEmailAsync(normalized, cancellationToken);
                if (winner is not null) return await AcknowledgeRegistrationAsync(winner);
            }
            return IdentityOperation<RegistrationOutcome>.Failure("email_unavailable");
        }

        await store.AppendAuditAsync(
            userId,
            "USER_REGISTERED",
            "User",
            userId,
            correlationId,
            cancellationToken);
        await store.AppendDomainEventAsync(userId, "USER_REGISTERED", correlationId, cancellationToken);

        if (registrationKey is not null)
        {
            if (!registrationSecrets!.TryRegistrationFingerprint(userId, registrationKey.Value, normalized, password,
                    cleanDisplayName, profileLocale, profileTimezone, registrationKeyVersion!, out var fingerprint, invitationHash))
                return IdentityOperation<RegistrationOutcome>.Failure("identity_retry_key_unavailable");
            await registrationReplays!.SaveAsync(userId, registrationKey.Value, new IdentityRegistrationReplay(
                registrationKeyVersion!, fingerprint, verificationRecord?.Id, verificationSource, verificationKeyVersion,
                now.AddHours(24)), cancellationToken);
        }

        if (invitationProof is not null && invitationRegistrations!.RequiresFinalCheck
            && !await invitationRegistrations.CheckAsync(invitationProof, normalized, cancellationToken))
            return IdentityOperation<RegistrationOutcome>.Failure("invalid_or_expired_invitation");
        return IdentityOperation<RegistrationOutcome>.Success(
            new RegistrationOutcome(ToProfile(user), verificationToken));

        async Task<IdentityOperation<RegistrationOutcome>> AcknowledgeRegistrationAsync(UserIdentity existing)
        {
            // Password and lifecycle proof precede any receipt or private-profile disclosure.
            if (!passwordHashes.Verify(existing.Id, existing.PasswordHash, password).IsValid
                || existing.Status is not (AccountStatus.Active or AccountStatus.PendingVerification))
                return IdentityOperation<RegistrationOutcome>.Failure("email_unavailable");
            var prior = await registrationReplays!.ReadAsync(existing.Id, registrationKey!.Value, cancellationToken);
            if (prior is null) return IdentityOperation<RegistrationOutcome>.Failure("email_unavailable");
            if (prior.ExpiresAt <= clock.UtcNow) return IdentityOperation<RegistrationOutcome>.Failure("idempotency_key_expired");
            if (!registrationSecrets!.TryRegistrationFingerprint(existing.Id, registrationKey.Value, normalized, password,
                    cleanDisplayName, profileLocale, profileTimezone, prior.KeyVersion, out var candidate, invitationHash))
                return IdentityOperation<RegistrationOutcome>.Failure("identity_retry_key_unavailable");
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(prior.Fingerprint)))
                return IdentityOperation<RegistrationOutcome>.Failure("idempotency_key_reused");
            string? originalVerification = null;
            if (!existing.EmailVerified && prior.VerificationTokenId is Guid tokenId)
            {
                if (prior.VerificationSource == RegistrationVerificationSource.ApiRegistration)
                {
                    if (!registrationSecrets.TryDeriveVerification(existing.Id, tokenId, prior.VerificationKeyVersion!, out originalVerification))
                        return IdentityOperation<RegistrationOutcome>.Failure("identity_retry_key_unavailable");
                }
                else if (prior.VerificationSource == RegistrationVerificationSource.EmailDelivery && deliverySigner is not null)
                {
                    try { originalVerification = deliverySigner.Derive(tokenId, IdentityTokenPurpose.VerifyEmail, prior.VerificationKeyVersion!); }
                    catch (InvalidOperationException) { return IdentityOperation<RegistrationOutcome>.Failure("identity_retry_key_unavailable"); }
                }
                else return IdentityOperation<RegistrationOutcome>.Failure("identity_retry_key_unavailable");
                if (await store.GetEmailVerificationUserIdAsync(tokens.Hash(originalVerification), clock.UtcNow, cancellationToken) != existing.Id)
                    return IdentityOperation<RegistrationOutcome>.Failure("idempotency_key_expired");
            }
            if (prior.ExpiresAt <= clock.UtcNow) return IdentityOperation<RegistrationOutcome>.Failure("idempotency_key_expired");
            if (invitationProof is not null && invitationRegistrations!.RequiresFinalCheck
                && !await invitationRegistrations.CheckAsync(invitationProof, normalized, cancellationToken))
                return IdentityOperation<RegistrationOutcome>.Failure("invalid_or_expired_invitation");
            return IdentityOperation<RegistrationOutcome>.Success(new(ToProfile(existing), originalVerification));
        }
    }

    public async Task<IdentityOperation<LoginOutcome>> LoginAsync(
        string email,
        string password,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null || string.IsNullOrWhiteSpace(password))
        {
            return IdentityOperation<LoginOutcome>.Failure("invalid_credentials");
        }

        var user = await store.FindUserByNormalizedEmailAsync(
            normalized,
            cancellationToken);

        if (user is null)
        {
            _ = passwordHashes.Verify(Guid.Empty, _unknownAccountHash, password);
            return IdentityOperation<LoginOutcome>.Failure("invalid_credentials");
        }

        var verification = passwordHashes.Verify(
            user.Id,
            user.PasswordHash,
            password);

        if (!verification.IsValid)
        {
            return IdentityOperation<LoginOutcome>.Failure("invalid_credentials");
        }

        if (user.Status == AccountStatus.PendingVerification || (policy.RequireVerifiedEmail && !user.EmailVerified))
        {
            return IdentityOperation<LoginOutcome>.Failure(
                "email_verification_required");
        }

        if (user.Status != AccountStatus.Active)
        {
            return IdentityOperation<LoginOutcome>.Failure("account_unavailable");
        }

        var intentKey = context?.IdempotencyKey;
        if (intentKey is not null)
        {
            if (loginReplays is null || loginSecrets is null) return IdentityOperation<LoginOutcome>.Failure("identity_storage_unavailable");
            var prior = await loginReplays.ReadAsync(user.Id, intentKey.Value, cancellationToken);
            if (prior is not null)
            {
                if (prior.ExpiresAt <= clock.UtcNow) return IdentityOperation<LoginOutcome>.Failure("idempotency_key_expired");
                if (!loginSecrets.TryFingerprint(user.Id, intentKey.Value, normalized, password, prior.KeyVersion, out var fingerprint)
                    || !loginSecrets.TryDeriveSession(user.Id, prior.SessionId, prior.KeyVersion, out var originalToken))
                    return IdentityOperation<LoginOutcome>.Failure("identity_retry_key_unavailable");
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(fingerprint), Encoding.ASCII.GetBytes(prior.Fingerprint)))
                    return IdentityOperation<LoginOutcome>.Failure("idempotency_key_reused");
                var original = await store.FindActiveSessionAsync(tokens.Hash(originalToken), clock.UtcNow, cancellationToken);
                if (original is null || original.SessionId != prior.SessionId || original.User.Id != user.Id || original.ExpiresAt <= clock.UtcNow)
                    return IdentityOperation<LoginOutcome>.Failure("idempotency_key_expired");
                return IdentityOperation<LoginOutcome>.Success(new LoginOutcome(ToProfile(original.User), originalToken, original.ExpiresAt));
            }
        }

        if (verification.NeedsRehash)
        {
            var upgradedHash = passwordHashes.Hash(user.Id, password);
            await store.UpdatePasswordHashAsync(
                user.Id,
                upgradedHash,
                clock.UtcNow,
                cancellationToken);

            user = user with
            {
                PasswordHash = upgradedHash,
                UpdatedAt = clock.UtcNow,
                Version = user.Version + 1,
            };
        }

        var sessionId = Guid.NewGuid();
        var rawSessionToken = tokens.Generate();
        string? intentFingerprint = null;
        if (intentKey is not null)
        {
            if (!loginSecrets!.TryDeriveSession(user.Id, sessionId, loginSecrets.CurrentKeyVersion, out rawSessionToken)
                || !loginSecrets.TryFingerprint(user.Id, intentKey.Value, normalized, password, loginSecrets.CurrentKeyVersion, out intentFingerprint))
                return IdentityOperation<LoginOutcome>.Failure("identity_retry_key_unavailable");
        }
        // PostgreSQL stores microseconds. The first acknowledgment must use the same
        // expiry precision as durable replay, rather than losing a tick on persistence.
        var now = new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
        var session = new SessionRecord(
            sessionId,
            user.Id,
            tokens.Hash(rawSessionToken),
            now,
            now.Add(policy.SessionLifetime));

        await store.CreateSessionAsync(session, cancellationToken);
        await store.AppendAuditAsync(
            user.Id,
            "SESSION_CREATED",
            "Session",
            session.Id,
            correlationId,
            cancellationToken);

        if (intentKey is not null)
        {
            if (session.ExpiresAt <= clock.UtcNow) return IdentityOperation<LoginOutcome>.Failure("session_unavailable");
            await loginReplays!.SaveAsync(user.Id, intentKey.Value,
                new IdentityLoginReplay(session.Id, loginSecrets!.CurrentKeyVersion, intentFingerprint!, now.AddHours(24)), cancellationToken);
        }

        // Session/receipt writes can wait. Re-admit the actual session after them
        // before disclosing its cookie; refusal rolls back the owning transaction.
        var admitted = await store.FindActiveSessionAsync(tokens.Hash(rawSessionToken), clock.UtcNow, cancellationToken);
        if (admitted is null || admitted.SessionId != sessionId || admitted.User.Id != user.Id
            || admitted.ExpiresAt <= clock.UtcNow || admitted.User.Status != AccountStatus.Active
            || (policy.RequireVerifiedEmail && !admitted.User.EmailVerified))
            return IdentityOperation<LoginOutcome>.Failure("session_unavailable");
        return IdentityOperation<LoginOutcome>.Success(
            new LoginOutcome(
                ToProfile(admitted.User),
                rawSessionToken,
                session.ExpiresAt));
    }

    public Task<AuthenticatedSession?> AuthenticateSessionAsync(
        string rawSessionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionToken))
        {
            return Task.FromResult<AuthenticatedSession?>(null);
        }

        return store.FindActiveSessionAsync(
            tokens.Hash(rawSessionToken),
            clock.UtcNow,
            cancellationToken);
    }

    public async Task<IdentityOperation<bool>> LogoutAsync(
        string rawSessionToken,
        Guid actorId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(rawSessionToken))
        {
            await store.RevokeSessionAsync(
                tokens.Hash(rawSessionToken),
                clock.UtcNow,
                cancellationToken);
        }

        await store.AppendAuditAsync(
            actorId,
            "SESSION_REVOKED",
            "User",
            actorId,
            correlationId,
            cancellationToken);
        await store.AppendDomainEventAsync(actorId, "SESSION_REVOKED", correlationId, cancellationToken);
        return IdentityOperation<bool>.Success(true);
    }

    public async Task<PasswordResetRequestOutcome> RequestPasswordResetAsync(
        string email,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return new PasswordResetRequestOutcome(await RequestRecoveryAsync(email, IdentityTokenPurpose.ResetPassword, correlationId, cancellationToken));
    }

    public async Task<string?> RequestEmailVerificationAsync(string email,string correlationId,CancellationToken cancellationToken=default)
    {
        return await RequestRecoveryAsync(email, IdentityTokenPurpose.VerifyEmail, correlationId, cancellationToken);
    }

    private async Task<string?> RequestRecoveryAsync(string email, IdentityTokenPurpose purpose, string correlationId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null) return null;
        var user = await store.FindUserByNormalizedEmailAsync(normalized, cancellationToken);
        if (user is null || user.Status is AccountStatus.Deactivated or AccountStatus.Suspended
            || (purpose == IdentityTokenPurpose.VerifyEmail && (user.Status != AccountStatus.PendingVerification || user.EmailVerified))) return null;
        var key = context?.IdempotencyKey;
        if (key is not null)
        {
            if (recoveryReplays is null || recoverySecrets is null) throw new InvalidOperationException("Recovery retry storage is unavailable.");
            var prior = await recoveryReplays.ReadAsync(user.Id, key.Value, purpose, cancellationToken);
            if (prior is not null)
            {
                // Every denial remains the same accepted response as an unknown email.
                // A used, expired or unverifiable intent never publishes a replacement.
                if (prior.ExpiresAt <= clock.UtcNow
                    || !recoverySecrets.TryRecoveryFingerprint(user.Id, key.Value, purpose, normalized, prior.KeyVersion, out var candidate)
                    || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(prior.Fingerprint))) return null;
                string? original;
                if (prior.TokenSource == RecoveryTokenSource.ApiRequest)
                {
                    if (!recoverySecrets.TryDeriveRecovery(user.Id, prior.TokenId, purpose, prior.TokenKeyVersion, out original)) return null;
                }
                else if (prior.TokenSource == RecoveryTokenSource.EmailDelivery && deliverySigner is not null)
                {
                    try { original = deliverySigner.Derive(prior.TokenId, purpose, prior.TokenKeyVersion); }
                    catch (InvalidOperationException) { return null; }
                }
                else return null;
                var subject = purpose == IdentityTokenPurpose.ResetPassword
                    ? await store.GetPasswordResetUserIdAsync(tokens.Hash(original), clock.UtcNow, cancellationToken)
                    : await store.GetEmailVerificationUserIdAsync(tokens.Hash(original), clock.UtcNow, cancellationToken);
                return subject == user.Id && prior.ExpiresAt > clock.UtcNow ? original : null;
            }
        }
        var tokenId = Guid.NewGuid();
        var now = new DateTimeOffset(clock.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero);
        var generated = tokens.GenerateForDelivery(tokenId, purpose, correlationId);
        var rawToken = generated.RawToken;
        var version = key is null ? null : recoverySecrets!.CurrentKeyVersion;
        if (key is not null && generated.Delivery is null
            && !recoverySecrets!.TryDeriveRecovery(user.Id, tokenId, purpose, version!, out rawToken)) return null;
        var record = new SecurityTokenRecord(tokenId, user.Id, tokens.Hash(rawToken), now, now.Add(policy.SecurityTokenLifetime));
        if (purpose == IdentityTokenPurpose.ResetPassword) await store.CreatePasswordResetTokenAsync(record, generated.Delivery, cancellationToken);
        else await store.CreateEmailVerificationTokenAsync(record, generated.Delivery, cancellationToken);
        await store.AppendAuditAsync(user.Id, purpose == IdentityTokenPurpose.ResetPassword ? "PASSWORD_RESET_REQUESTED" : "EMAIL_VERIFICATION_REQUESTED", "User", user.Id, correlationId, cancellationToken);
        if (key is not null)
        {
            if (!recoverySecrets!.TryRecoveryFingerprint(user.Id, key.Value, purpose, normalized, version!, out var fingerprint))
                throw new InvalidOperationException("Recovery retry signing is unavailable.");
            await recoveryReplays!.SaveAsync(user.Id, key.Value, purpose, new IdentityRecoveryRequestReplay(version!, fingerprint, tokenId,
                generated.Delivery is null ? RecoveryTokenSource.ApiRequest : RecoveryTokenSource.EmailDelivery,
                generated.Delivery?.KeyId ?? version!, now.AddHours(24)), cancellationToken);
        }
        return rawToken;
    }

    public Task<IdentityOperation<UserProfile>> ResetPasswordAsync(string rawResetToken, string newPassword, string correlationId,
        CancellationToken cancellationToken = default) => context?.IdempotencyKey is null
        ? ResetPasswordCoreAsync(rawResetToken, newPassword, correlationId, cancellationToken)
        : RetryTokenConsumptionAsync(rawResetToken, newPassword, IdentityTokenPurpose.ResetPassword,
            () => ResetPasswordCoreAsync(rawResetToken, newPassword, correlationId, cancellationToken), cancellationToken);

    private async Task<IdentityOperation<UserProfile>> ResetPasswordCoreAsync(
        string rawResetToken,
        string newPassword,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPassword(newPassword))
        {
            return IdentityOperation<UserProfile>.Failure("invalid_password");
        }

        if (string.IsNullOrWhiteSpace(rawResetToken))
        {
            return IdentityOperation<UserProfile>.Failure(
                "invalid_or_expired_token");
        }

        var tokenHash = tokens.Hash(rawResetToken);
        var placeholderUserId = Guid.Empty;
        var newHash = passwordHashes.Hash(placeholderUserId, newPassword);

        var userId = await store.GetPasswordResetUserIdAsync(
            tokenHash,
            clock.UtcNow,
            cancellationToken);

        if (userId is null)
        {
            return IdentityOperation<UserProfile>.Failure(
                "invalid_or_expired_token");
        }

        newHash = passwordHashes.Hash(userId.Value, newPassword);

        if (!await store.CompletePasswordResetAsync(
                tokenHash,
                newHash,
                clock.UtcNow,
                cancellationToken))
        {
            return IdentityOperation<UserProfile>.Failure(
                "invalid_or_expired_token");
        }

        var user = await store.FindUserByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return IdentityOperation<UserProfile>.Failure("account_unavailable");
        }

        await store.AppendAuditAsync(
            user.Id,
            "PASSWORD_RESET_COMPLETED",
            "User",
            user.Id,
            correlationId,
            cancellationToken);
        await store.AppendDomainEventAsync(user.Id, "SESSION_REVOKED", correlationId, cancellationToken);

        return IdentityOperation<UserProfile>.Success(ToProfile(user));
    }

    public Task<IdentityOperation<UserProfile>> VerifyEmailAsync(string rawVerificationToken, string correlationId,
        CancellationToken cancellationToken = default) => context?.IdempotencyKey is null
        ? VerifyEmailCoreAsync(rawVerificationToken, correlationId, cancellationToken)
        : RetryTokenConsumptionAsync(rawVerificationToken, null, IdentityTokenPurpose.VerifyEmail,
            () => VerifyEmailCoreAsync(rawVerificationToken, correlationId, cancellationToken), cancellationToken);

    private async Task<IdentityOperation<UserProfile>> VerifyEmailCoreAsync(
        string rawVerificationToken,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawVerificationToken))
        {
            return IdentityOperation<UserProfile>.Failure(
                "invalid_or_expired_token");
        }

        var tokenHash = tokens.Hash(rawVerificationToken);
        var userId = await store.GetEmailVerificationUserIdAsync(
            tokenHash,
            clock.UtcNow,
            cancellationToken);

        if (userId is null ||
            !await store.VerifyEmailAsync(
                tokenHash,
                clock.UtcNow,
                cancellationToken))
        {
            return IdentityOperation<UserProfile>.Failure(
                "invalid_or_expired_token");
        }

        var user = await store.FindUserByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return IdentityOperation<UserProfile>.Failure("account_unavailable");
        }

        await store.AppendAuditAsync(
            user.Id,
            "EMAIL_VERIFIED",
            "User",
            user.Id,
            correlationId,
            cancellationToken);
        await store.AppendDomainEventAsync(user.Id, "EMAIL_VERIFIED", correlationId, cancellationToken);

        return IdentityOperation<UserProfile>.Success(ToProfile(user));
    }

    private async Task<IdentityOperation<UserProfile>> RetryTokenConsumptionAsync(string rawToken, string? newPassword,
        IdentityTokenPurpose purpose, Func<Task<IdentityOperation<UserProfile>>> consume, CancellationToken cancellationToken)
    {
        if (purpose == IdentityTokenPurpose.ResetPassword && !IsValidPassword(newPassword!))
            return IdentityOperation<UserProfile>.Failure("invalid_password");
        if (string.IsNullOrWhiteSpace(rawToken)) return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
        if (consumptionReplays is null || consumptionSecrets is null) return IdentityOperation<UserProfile>.Failure("identity_storage_unavailable");
        var hash = tokens.Hash(rawToken);
        var proof = await store.FindSecurityTokenRetryProofAsync(hash, purpose, clock.UtcNow, cancellationToken);
        if (proof is null || proof.ExpiresAt <= clock.UtcNow || proof.User.Status == AccountStatus.Deactivated
            || (purpose == IdentityTokenPurpose.ResetPassword && proof.User.Status == AccountStatus.Suspended))
            return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
        if (proof.UsedAt is not null && (purpose == IdentityTokenPurpose.ResetPassword
                ? !passwordHashes.Verify(proof.User.Id, proof.User.PasswordHash, newPassword!).IsValid
                : proof.User.Status != AccountStatus.Active || !proof.User.EmailVerified))
            return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
        var key = context!.IdempotencyKey!.Value;
        var prior = await consumptionReplays.ReadAsync(proof.User.Id, key, purpose, cancellationToken);
        if (prior is not null)
        {
            if (proof.UsedAt is null || prior.TokenId != proof.TokenId || prior.ConsumedAt != proof.UsedAt
                || prior.ExpiresAt <= clock.UtcNow) return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
            if (!consumptionSecrets.TryConsumptionFingerprint(proof.User.Id, key, purpose, rawToken, newPassword, prior.KeyVersion, out var candidate))
                return IdentityOperation<UserProfile>.Failure("identity_retry_key_unavailable");
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(prior.Fingerprint)))
                return IdentityOperation<UserProfile>.Failure("idempotency_key_reused");
            if (proof.ExpiresAt <= clock.UtcNow || prior.ExpiresAt <= clock.UtcNow)
                return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
            // Acknowledgment only: never repeat token consumption or revoke newer sessions.
            return IdentityOperation<UserProfile>.Success(ToProfile(proof.User));
        }
        if (proof.UsedAt is not null) return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
        var version = consumptionSecrets.CurrentKeyVersion;
        if (!consumptionSecrets.TryConsumptionFingerprint(proof.User.Id, key, purpose, rawToken, newPassword, version, out var fingerprint))
            return IdentityOperation<UserProfile>.Failure("identity_retry_key_unavailable");
        var result = await consume();
        if (!result.Succeeded || result.Value is null) return result;
        var consumed = await store.FindSecurityTokenRetryProofAsync(hash, purpose, clock.UtcNow, cancellationToken);
        if (consumed?.UsedAt is not DateTimeOffset consumedAt || consumed.TokenId != proof.TokenId || consumed.ExpiresAt <= clock.UtcNow)
            return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
        var expiry = consumed.ExpiresAt < clock.UtcNow.AddHours(24) ? consumed.ExpiresAt : clock.UtcNow.AddHours(24);
        await consumptionReplays.SaveAsync(proof.User.Id, key, purpose, new(proof.TokenId, version, fingerprint, consumedAt, expiry), cancellationToken);
        // Receipt publication can wait beyond the proof lifetime. Refuse before
        // profile disclosure so the owning transaction restores consumption.
        if (consumed.ExpiresAt <= clock.UtcNow || expiry <= clock.UtcNow)
            return IdentityOperation<UserProfile>.Failure("invalid_or_expired_token");
        return result;
    }

    public async Task<IdentityOperation<UserProfile>> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? avatarUrl,
        string? locale,
        string? timezone,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.FindUserByIdAsync(userId, cancellationToken);
        if (existing is null || existing.Status == AccountStatus.Deactivated)
        {
            return IdentityOperation<UserProfile>.Failure("account_unavailable");
        }

        var nextDisplayName = displayName?.Trim() ?? existing.DisplayName;
        if (expectedVersion < 1)
        {
            return IdentityOperation<UserProfile>.Failure("invalid_version");
        }
        if (existing.Version != expectedVersion)
        {
            return IdentityOperation<UserProfile>.Failure("version_conflict");
        }
        if (string.IsNullOrWhiteSpace(nextDisplayName) || nextDisplayName.Length > 120)
        {
            return IdentityOperation<UserProfile>.Failure("invalid_display_name");
        }

        var nextAvatarUrl = avatarUrl is null ? existing.AvatarUrl : avatarUrl.Trim();
        if (!string.IsNullOrEmpty(nextAvatarUrl) &&
            (nextAvatarUrl.Length > 2048 ||
             !Uri.TryCreate(nextAvatarUrl, UriKind.Absolute, out var avatarUri) ||
             avatarUri.Scheme != Uri.UriSchemeHttps ||
             !string.IsNullOrEmpty(avatarUri.UserInfo)))
        {
            return IdentityOperation<UserProfile>.Failure("invalid_avatar_url");
        }

        var nextLocale = (locale ?? existing.Locale).Trim();
        var nextTimezone = (timezone ?? existing.Timezone).Trim();
        if (!TryNormalizeLocale(nextLocale, out nextLocale))
        {
            return IdentityOperation<UserProfile>.Failure("invalid_locale");
        }
        if (!TryNormalizeTimezone(nextTimezone, out nextTimezone))
        {
            return IdentityOperation<UserProfile>.Failure("invalid_timezone");
        }

        var updated = await store.UpdateProfileAsync(
            userId,
            nextDisplayName,
            string.IsNullOrEmpty(nextAvatarUrl) ? null : nextAvatarUrl,
            nextLocale,
            nextTimezone,
            expectedVersion,
            clock.UtcNow,
            cancellationToken);

        if (updated is null)
        {
            return IdentityOperation<UserProfile>.Failure("version_conflict");
        }

        await store.AppendAuditAsync(
            userId,
            "USER_PROFILE_UPDATED",
            "User",
            userId,
            correlationId,
            cancellationToken);
        await store.AppendDomainEventAsync(userId, "USER_PROFILE_UPDATED", correlationId, cancellationToken);

        return IdentityOperation<UserProfile>.Success(ToProfile(updated));
    }

    public async Task<IdentityOperation<bool>> DeactivateAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var deactivated = await store.DeactivateUserAsync(
            userId,
            clock.UtcNow,
            cancellationToken);

        if (deactivated)
        {
            await store.AppendAuditAsync(
                userId,
                "USER_DEACTIVATED",
                "User",
                userId,
                correlationId,
                cancellationToken);
            await store.AppendDomainEventAsync(userId, "USER_DEACTIVATED", correlationId, cancellationToken);
        }

        return deactivated ? IdentityOperation<bool>.Success(true) : IdentityOperation<bool>.Failure("account_unavailable");
    }

    private bool IsValidPassword(string password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= policy.MinimumPasswordLength;

    private static string? NormalizeEmail(string email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            !MailAddress.TryCreate(trimmed, out var parsed) ||
            !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }

    private static string NormalizeLocale(string? locale) =>
        string.IsNullOrWhiteSpace(locale) ? "en-CA" : locale.Trim();

    private static string NormalizeTimezone(string? timezone) =>
        string.IsNullOrWhiteSpace(timezone) ? "America/Vancouver" : timezone.Trim();

    private static bool TryNormalizeLocale(string value, out string normalized)
    {
        normalized = value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(value);
            if (culture.IsNeutralCulture) return false;
            normalized = culture.Name;
            return true;
        }
        catch (System.Globalization.CultureNotFoundException) { return false; }
    }

    private static bool TryNormalizeTimezone(string value, out string normalized)
    {
        normalized = value;
        if (value.Length > 128 || !TimeZoneInfo.TryFindSystemTimeZoneById(value, out var zone)) return false;
        normalized = zone.Id == "UTC" ? "UTC" : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
        return true;
    }

    private static UserProfile ToProfile(UserIdentity user) =>
        new(
            user.Id,
            user.Email,
            user.DisplayName,
            user.AvatarUrl,
            user.Locale,
            TryNormalizeTimezone(user.Timezone, out var timezone) ? timezone : user.Timezone,
            user.Status,
            user.EmailVerified,
            user.CreatedAt,
            user.UpdatedAt,
            user.Version);
}
