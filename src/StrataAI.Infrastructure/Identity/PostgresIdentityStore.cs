using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityStore(
    PostgresConnectionFactory connectionFactory) : IIdentityStore
{
    private const string UserColumns = """
        id, email, email_normalized, display_name, avatar_url, locale, timezone,
        status, email_verified, password_hash, created_at, updated_at, version
        """;

    public async Task<bool> TryCreateUserAsync(
        UserIdentity user,
        SecurityTokenRecord? verificationToken,
        IdentityTokenDelivery? delivery,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users(
                id, email, email_normalized, display_name, avatar_url,
                locale, timezone, status, email_verified, password_hash,
                created_at, updated_at, version)
            VALUES (
                @id, @email, @email_normalized, @display_name, @avatar_url,
                @locale, @timezone, @status, @email_verified, @password_hash,
                @created_at, @updated_at, @version)
            ON CONFLICT (email_normalized) DO NOTHING;
            """,
            connection, transaction);

        AddUserParameters(command, user);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        if (verificationToken is not null)
        {
            if (verificationToken.UserId != user.Id) throw new ArgumentException("Verification subject must match the new user.");
            await InsertSecurityTokenAsync("email_verification_tokens", verificationToken, connection, transaction, cancellationToken);
            if (delivery is not null)
                await PublishIdentityDeliveryAsync(verificationToken, delivery, IdentityTokenPurpose.VerifyEmail, connection, transaction, cancellationToken);
        }
        else if (delivery is not null) throw new ArgumentException("Delivery requires a verification token.");
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<UserIdentity?> FindUserByNormalizedEmailAsync(
        string emailNormalized,
        CancellationToken cancellationToken = default) =>
        FindOneAsync(
            $"SELECT {UserColumns} FROM users WHERE email_normalized = @value;",
            emailNormalized,
            cancellationToken);

    public async Task<UserIdentity?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT {UserColumns} FROM users WHERE id = @id" + (routing.Transaction is null ? ";" : " FOR SHARE;"),
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("id", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadUser(reader)
            : null;
    }

    public async Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE users
            SET password_hash = @password_hash,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("password_hash", passwordHash);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateSessionAsync(
        SessionRecord session,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO sessions(id, user_id, token_hash, created_at, expires_at)
            VALUES (@id, @user_id, @token_hash, @created_at, @expires_at);
            """,
            connection);
        command.Parameters.AddWithValue("id", session.Id);
        command.Parameters.AddWithValue("user_id", session.UserId);
        command.Parameters.AddWithValue("token_hash", session.TokenHash);
        command.Parameters.AddWithValue("created_at", session.CreatedAt);
        command.Parameters.AddWithValue("expires_at", session.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthenticatedSession?> FindActiveSessionAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT
                s.id,
                s.expires_at,
                {string.Join(", ", UserColumns.Split(", ").Select(c => "u." + c.Trim()))}
            FROM sessions s
            JOIN users u ON u.id = s.user_id
            WHERE s.token_hash = @token_hash
              AND s.revoked_at IS NULL
              AND s.expires_at > @now
              AND s.expires_at > clock_timestamp()
              AND u.status = 'ACTIVE'
            """ + (routing.Transaction is null ? ";" : " FOR SHARE OF s;"),
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("now", now);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var sessionId = reader.GetGuid(0);
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(1);
        var user = ReadUser(reader, offset: 2);
        return new AuthenticatedSession(sessionId, user, expiresAt);
    }

    public async Task RevokeSessionAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE sessions
            SET revoked_at = COALESCE(revoked_at, @revoked_at)
            WHERE token_hash = @token_hash;
            """,
            connection);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("revoked_at", revokedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CreatePasswordResetTokenAsync(
        SecurityTokenRecord token,
        IdentityTokenDelivery? delivery,
        CancellationToken cancellationToken = default) =>
        CreateSecurityTokenAsync(
            "password_reset_tokens",
            token,
            delivery,
            IdentityTokenPurpose.ResetPassword,
            cancellationToken);

    public Task<Guid?> GetPasswordResetUserIdAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        GetActiveSecurityTokenUserIdAsync(
            "password_reset_tokens",
            tokenHash,
            now,
            cancellationToken);

    public async Task<bool> CompletePasswordResetAsync(
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using var lookup = new NpgsqlCommand(
            """
            SELECT t.user_id
            FROM password_reset_tokens t
            JOIN users u ON u.id = t.user_id
            WHERE t.token_hash = @token_hash
              AND t.used_at IS NULL
              AND t.revoked_at IS NULL
              AND t.expires_at > @used_at
              AND u.status NOT IN ('SUSPENDED', 'DEACTIVATED')
            FOR UPDATE OF t;
            """,
            connection,
            transaction);
        lookup.Parameters.AddWithValue("token_hash", tokenHash);
        lookup.Parameters.AddWithValue("used_at", usedAt);

        var userIdObject = await lookup.ExecuteScalarAsync(cancellationToken);
        if (userIdObject is not Guid userId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateUser = new NpgsqlCommand(
            """
            UPDATE users
            SET password_hash = @password_hash,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @user_id;
            """,
            connection,
            transaction);
        updateUser.Parameters.AddWithValue("password_hash", newPasswordHash);
        updateUser.Parameters.AddWithValue("updated_at", usedAt);
        updateUser.Parameters.AddWithValue("user_id", userId);
        await updateUser.ExecuteNonQueryAsync(cancellationToken);

        await using var consume = new NpgsqlCommand(
            """
            UPDATE password_reset_tokens
            SET used_at = @used_at
            WHERE token_hash = @token_hash;
            """,
            connection,
            transaction);
        consume.Parameters.AddWithValue("used_at", usedAt);
        consume.Parameters.AddWithValue("token_hash", tokenHash);
        await consume.ExecuteNonQueryAsync(cancellationToken);

        await using var revokeSessions = new NpgsqlCommand(
            """
            UPDATE sessions
            SET revoked_at = COALESCE(revoked_at, @revoked_at)
            WHERE user_id = @user_id AND revoked_at IS NULL;
            """,
            connection,
            transaction);
        revokeSessions.Parameters.AddWithValue("revoked_at", usedAt);
        revokeSessions.Parameters.AddWithValue("user_id", userId);
        await revokeSessions.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task CreateEmailVerificationTokenAsync(
        SecurityTokenRecord token,
        IdentityTokenDelivery? delivery,
        CancellationToken cancellationToken = default) =>
        CreateSecurityTokenAsync(
            "email_verification_tokens",
            token,
            delivery,
            IdentityTokenPurpose.VerifyEmail,
            cancellationToken);

    public Task<Guid?> GetEmailVerificationUserIdAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        GetActiveSecurityTokenUserIdAsync(
            "email_verification_tokens",
            tokenHash,
            now,
            cancellationToken);

    public async Task<bool> VerifyEmailAsync(
        string tokenHash,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using var lookup = new NpgsqlCommand(
            """
            SELECT user_id
            FROM email_verification_tokens
            WHERE token_hash = @token_hash
              AND used_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > @used_at
            FOR UPDATE;
            """,
            connection,
            transaction);
        lookup.Parameters.AddWithValue("token_hash", tokenHash);
        lookup.Parameters.AddWithValue("used_at", usedAt);
        var userIdObject = await lookup.ExecuteScalarAsync(cancellationToken);
        if (userIdObject is not Guid userId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var updateUser = new NpgsqlCommand(
            """
            UPDATE users
            SET email_verified = true,
                status = CASE
                    WHEN status = 'PENDING_VERIFICATION' THEN 'ACTIVE'
                    ELSE status
                END,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @user_id AND status <> 'DEACTIVATED';
            """,
            connection,
            transaction);
        updateUser.Parameters.AddWithValue("updated_at", usedAt);
        updateUser.Parameters.AddWithValue("user_id", userId);
        if (await updateUser.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var consume = new NpgsqlCommand(
            """
            UPDATE email_verification_tokens
            SET used_at = @used_at
            WHERE token_hash = @token_hash;
            """,
            connection,
            transaction);
        consume.Parameters.AddWithValue("used_at", usedAt);
        consume.Parameters.AddWithValue("token_hash", tokenHash);
        await consume.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<UserIdentity?> UpdateProfileAsync(
        Guid userId,
        string displayName,
        string? avatarUrl,
        string locale,
        string timezone,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE users
            SET display_name = @display_name,
                avatar_url = @avatar_url,
                locale = @locale,
                timezone = @timezone,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @id
              AND status <> 'DEACTIVATED'
              AND version = @expected_version
            RETURNING {UserColumns};
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue(
            "avatar_url",
            avatarUrl is null ? DBNull.Value : avatarUrl);
        command.Parameters.AddWithValue("locale", locale);
        command.Parameters.AddWithValue("timezone", timezone);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        command.Parameters.AddWithValue("updated_at", updatedAt);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadUser(reader)
            : null;
    }

    public async Task<bool> DeactivateUserAsync(
        Guid userId,
        DateTimeOffset deactivatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var ownedTransaction = routing.Transaction is null
            ? await routing.Connection.BeginTransactionAsync(cancellationToken) : null;
        var transaction = routing.Transaction ?? ownedTransaction!;

        await using var updateUser = new NpgsqlCommand(
            """
            UPDATE users
            SET status = 'DEACTIVATED',
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @id
              AND status <> 'DEACTIVATED';
            """,
            routing.Connection,
            transaction);
        updateUser.Parameters.AddWithValue("id", userId);
        updateUser.Parameters.AddWithValue("updated_at", deactivatedAt);
        var changed = await updateUser.ExecuteNonQueryAsync(cancellationToken) == 1;

        if (changed)
        {
            await using var revokeSessions = new NpgsqlCommand(
                """
                UPDATE sessions
                SET revoked_at = COALESCE(revoked_at, @revoked_at)
                WHERE user_id = @user_id AND revoked_at IS NULL;
                """,
                routing.Connection,
                transaction);
            revokeSessions.Parameters.AddWithValue("revoked_at", deactivatedAt);
            revokeSessions.Parameters.AddWithValue("user_id", userId);
            await revokeSessions.ExecuteNonQueryAsync(cancellationToken);
        }

        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task AppendAuditAsync(
        Guid? actorId,
        string eventType,
        string entityType,
        Guid? entityId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_events(
                id, tenant_id, actor_id, event_type, entity_type,
                entity_id, correlation_id, safe_metadata, created_at)
            VALUES (
                @id, NULL, @actor_id, @event_type, @entity_type,
                @entity_id, @correlation_id, '{}'::jsonb, now());
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "actor_id",
            actorId.HasValue ? actorId.Value : DBNull.Value);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue(
            "entity_id",
            entityId.HasValue ? entityId.Value : DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<UserIdentity?> FindOneAsync(
        string sql,
        string value,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadUser(reader)
            : null;
    }

    private async Task CreateSecurityTokenAsync(
        string tableName,
        SecurityTokenRecord token,
        IdentityTokenDelivery? delivery,
        IdentityTokenPurpose purpose,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertSecurityTokenAsync(tableName, token, connection, transaction, cancellationToken);
        if (delivery is not null)
            await PublishIdentityDeliveryAsync(token, delivery, purpose, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertSecurityTokenAsync(string tableName, SecurityTokenRecord token,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO {tableName}(
                id, user_id, token_hash, created_at, expires_at)
            VALUES (
                @id, @user_id, @token_hash, @created_at, @expires_at);
            """,
            connection, transaction);
        command.Parameters.AddWithValue("id", token.Id);
        command.Parameters.AddWithValue("user_id", token.UserId);
        command.Parameters.AddWithValue("token_hash", token.TokenHash);
        command.Parameters.AddWithValue("created_at", token.CreatedAt);
        command.Parameters.AddWithValue("expires_at", token.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PublishIdentityDeliveryAsync(SecurityTokenRecord token, IdentityTokenDelivery delivery,
        IdentityTokenPurpose expectedPurpose, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (delivery.Purpose != expectedPurpose) throw new ArgumentException("Identity delivery purpose does not match the token.");
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',true);
            WITH publication AS (SELECT clock_timestamp() AS queued_at)
            INSERT INTO identity_delivery_jobs(id,user_id,purpose,password_reset_token_id,verification_token_id,
                key_id,correlation_id,recipient_email,sender_address,public_origin,provider_account,created_at,expires_at)
            SELECT @id,@user,@purpose,@reset,@verification,@key,@correlation,email,@sender,@origin,@account,queued_at,
                LEAST(@expires,queued_at + interval '23 hours') FROM users CROSS JOIN publication WHERE id=@user;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", token.Id);
        command.Parameters.AddWithValue("user", token.UserId);
        command.Parameters.AddWithValue("purpose", expectedPurpose == IdentityTokenPurpose.VerifyEmail ? "VERIFY_EMAIL" : "RESET_PASSWORD");
        command.Parameters.AddWithValue("reset", NpgsqlTypes.NpgsqlDbType.Uuid, expectedPurpose == IdentityTokenPurpose.ResetPassword ? token.Id : DBNull.Value);
        command.Parameters.AddWithValue("verification", NpgsqlTypes.NpgsqlDbType.Uuid, expectedPurpose == IdentityTokenPurpose.VerifyEmail ? token.Id : DBNull.Value);
        command.Parameters.AddWithValue("key", delivery.KeyId);
        command.Parameters.AddWithValue("correlation", delivery.CorrelationId);
        command.Parameters.AddWithValue("sender", delivery.SenderAddress);
        command.Parameters.AddWithValue("origin", delivery.PublicOrigin);
        command.Parameters.AddWithValue("account", delivery.ProviderAccount);
        command.Parameters.AddWithValue("expires", token.ExpiresAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Guid?> GetActiveSecurityTokenUserIdAsync(
        string tableName,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT user_id
            FROM {tableName}
            WHERE token_hash = @token_hash
              AND used_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > @now;
            """,
            connection);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("now", now);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid userId ? userId : null;
    }

    private static void AddUserParameters(
        NpgsqlCommand command,
        UserIdentity user)
    {
        command.Parameters.AddWithValue("id", user.Id);
        command.Parameters.AddWithValue("email", user.Email);
        command.Parameters.AddWithValue("email_normalized", user.EmailNormalized);
        command.Parameters.AddWithValue("display_name", user.DisplayName);
        command.Parameters.AddWithValue(
            "avatar_url",
            user.AvatarUrl is null ? DBNull.Value : user.AvatarUrl);
        command.Parameters.AddWithValue("locale", user.Locale);
        command.Parameters.AddWithValue("timezone", user.Timezone);
        command.Parameters.AddWithValue("status", ToDatabaseStatus(user.Status));
        command.Parameters.AddWithValue("email_verified", user.EmailVerified);
        command.Parameters.AddWithValue("password_hash", user.PasswordHash);
        command.Parameters.AddWithValue("created_at", user.CreatedAt);
        command.Parameters.AddWithValue("updated_at", user.UpdatedAt);
        command.Parameters.AddWithValue("version", user.Version);
    }

    private static UserIdentity ReadUser(
        NpgsqlDataReader reader,
        int offset = 0)
    {
        return new UserIdentity(
            reader.GetGuid(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            reader.GetString(offset + 5),
            reader.GetString(offset + 6),
            ParseStatus(reader.GetString(offset + 7)),
            reader.GetBoolean(offset + 8),
            reader.GetString(offset + 9),
            reader.GetFieldValue<DateTimeOffset>(offset + 10),
            reader.GetFieldValue<DateTimeOffset>(offset + 11),
            reader.GetInt64(offset + 12));
    }

    private static string ToDatabaseStatus(AccountStatus status) =>
        status switch
        {
            AccountStatus.PendingVerification => "PENDING_VERIFICATION",
            AccountStatus.Active => "ACTIVE",
            AccountStatus.Suspended => "SUSPENDED",
            AccountStatus.Deactivated => "DEACTIVATED",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static AccountStatus ParseStatus(string status) =>
        status switch
        {
            "PENDING_VERIFICATION" => AccountStatus.PendingVerification,
            "ACTIVE" => AccountStatus.Active,
            "SUSPENDED" => AccountStatus.Suspended,
            "DEACTIVATED" => AccountStatus.Deactivated,
            _ => throw new InvalidOperationException(
                $"Unknown account status '{status}'."),
        };
}
