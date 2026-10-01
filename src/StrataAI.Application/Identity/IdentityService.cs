using System.Net.Mail;
using StrataAI.Application.Common;

namespace StrataAI.Application.Identity;

public sealed class IdentityService(
    IIdentityStore store,
    IPasswordHashService passwordHashes,
    ISecureTokenService tokens,
    IClock clock,
    IdentityPolicy policy) : IIdentityService
{
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
        CancellationToken cancellationToken = default)
    {
        if (!policy.AllowSelfRegistration)
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

        var now = clock.UtcNow;
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
        if (policy.RequireVerifiedEmail)
        {
            var tokenId = Guid.NewGuid();
            var generated = tokens.GenerateForDelivery(tokenId, IdentityTokenPurpose.VerifyEmail, correlationId);
            verificationToken = generated.RawToken;
            verificationDelivery = generated.Delivery;
            verificationRecord = new SecurityTokenRecord(
                    tokenId,
                    userId,
                    tokens.Hash(verificationToken),
                    now,
                    now.Add(policy.SecurityTokenLifetime));
        }
        if (!await store.TryCreateUserAsync(user, verificationRecord, verificationDelivery, cancellationToken))
        {
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

        return IdentityOperation<RegistrationOutcome>.Success(
            new RegistrationOutcome(ToProfile(user), verificationToken));
    }

    public async Task<IdentityOperation<LoginOutcome>> LoginAsync(
        string email,
        string password,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
        {
            return IdentityOperation<LoginOutcome>.Failure("invalid_credentials");
        }

        var user = await store.FindUserByNormalizedEmailAsync(
            normalized,
            cancellationToken);

        if (user is null)
        {
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

        if (user.Status == AccountStatus.PendingVerification)
        {
            return IdentityOperation<LoginOutcome>.Failure(
                "email_verification_required");
        }

        if (user.Status != AccountStatus.Active)
        {
            return IdentityOperation<LoginOutcome>.Failure("account_unavailable");
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

        var rawSessionToken = tokens.Generate();
        var now = clock.UtcNow;
        var session = new SessionRecord(
            Guid.NewGuid(),
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

        return IdentityOperation<LoginOutcome>.Success(
            new LoginOutcome(
                ToProfile(user),
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
        var normalized = NormalizeEmail(email);
        if (normalized is null)
        {
            return new PasswordResetRequestOutcome(null);
        }

        var user = await store.FindUserByNormalizedEmailAsync(
            normalized,
            cancellationToken);

        if (user is null ||
            user.Status is AccountStatus.Deactivated or AccountStatus.Suspended)
        {
            return new PasswordResetRequestOutcome(null);
        }

        var tokenId = Guid.NewGuid();
        var generated = tokens.GenerateForDelivery(tokenId, IdentityTokenPurpose.ResetPassword, correlationId);
        var rawToken = generated.RawToken;
        var now = clock.UtcNow;

        await store.CreatePasswordResetTokenAsync(
            new SecurityTokenRecord(
                tokenId,
                user.Id,
                tokens.Hash(rawToken),
                now,
                now.Add(policy.SecurityTokenLifetime)),
            generated.Delivery,
            cancellationToken);

        await store.AppendAuditAsync(
            user.Id,
            "PASSWORD_RESET_REQUESTED",
            "User",
            user.Id,
            correlationId,
            cancellationToken);

        return new PasswordResetRequestOutcome(rawToken);
    }

    public async Task<string?> RequestEmailVerificationAsync(string email,string correlationId,CancellationToken cancellationToken=default)
    {
        var normalized=NormalizeEmail(email);
        if (normalized is null) return null;
        var user=await store.FindUserByNormalizedEmailAsync(normalized,cancellationToken);
        if (user is null || user.Status!=AccountStatus.PendingVerification || user.EmailVerified) return null;
        var id=Guid.NewGuid(); var now=clock.UtcNow;
        var generated=tokens.GenerateForDelivery(id,IdentityTokenPurpose.VerifyEmail,correlationId);
        await store.CreateEmailVerificationTokenAsync(new SecurityTokenRecord(id,user.Id,tokens.Hash(generated.RawToken),now,
            now.Add(policy.SecurityTokenLifetime)),generated.Delivery,cancellationToken);
        await store.AppendAuditAsync(user.Id,"EMAIL_VERIFICATION_REQUESTED","User",user.Id,correlationId,cancellationToken);
        return generated.RawToken;
    }

    public async Task<IdentityOperation<UserProfile>> ResetPasswordAsync(
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

    public async Task<IdentityOperation<UserProfile>> VerifyEmailAsync(
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
