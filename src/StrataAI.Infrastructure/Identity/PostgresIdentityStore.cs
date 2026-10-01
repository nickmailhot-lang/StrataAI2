using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Identity;

internal sealed class PostgresIdentityStore(
    PostgresConnectionFactory connectionFactory) : IIdentityStore
{
    public async Task<IdentitySecurityTokenProof?> FindSecurityTokenRetryProofAsync(string tokenHash, IdentityTokenPurpose purpose,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasIdentityCommandScope) throw new InvalidOperationException("Token retry proof requires an owning identity transaction.");
        var table = purpose switch { IdentityTokenPurpose.ResetPassword => "password_reset_tokens", IdentityTokenPurpose.VerifyEmail => "email_verification_tokens", _ => throw new ArgumentOutOfRangeException(nameof(purpose)) };
        await using var session = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        // Freeze the current user first, then the original token. Reread wall-clock expiry after the token wait.
        await using var route = new NpgsqlCommand($"SELECT u.id FROM users u JOIN {table} t ON t.user_id=u.id WHERE t.token_hash=@hash FOR UPDATE OF u;", session.Connection, session.Transaction);
        route.Parameters.AddWithValue("hash", tokenHash);
        if (await route.ExecuteScalarAsync(cancellationToken) is not Guid userId) return null;
        await using var freeze = new NpgsqlCommand($"SELECT id FROM {table} WHERE user_id=@user AND token_hash=@hash FOR SHARE;", session.Connection, session.Transaction);
        freeze.Parameters.AddWithValue("user", userId); freeze.Parameters.AddWithValue("hash", tokenHash);
        if (await freeze.ExecuteScalarAsync(cancellationToken) is not Guid tokenId) return null;
        await using var proof = new NpgsqlCommand($"SELECT expires_at,used_at FROM {table} WHERE id=@id AND user_id=@user AND revoked_at IS NULL AND expires_at>@now AND expires_at>clock_timestamp();", session.Connection, session.Transaction);
        proof.Parameters.AddWithValue("id", tokenId); proof.Parameters.AddWithValue("user", userId); proof.Parameters.AddWithValue("now", now);
        DateTimeOffset expiry; DateTimeOffset? used;
        await using (var reader = await proof.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            expiry = reader.GetFieldValue<DateTimeOffset>(0); used = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }
        var user = await FindUserByIdAsync(userId, cancellationToken);
        return user is null ? null : new(user, tokenId, expiry, used);
    }
    public async Task<RevocationSessionProof?> FindRevocationSessionProofAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasIdentityCommandScope) throw new InvalidOperationException("Revocation proof requires an identity transaction.");
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var lookup = new NpgsqlCommand("SELECT user_id FROM sessions WHERE token_hash=@hash;", routing.Connection, routing.Transaction);
        lookup.Parameters.AddWithValue("hash", tokenHash);
        if (await lookup.ExecuteScalarAsync(cancellationToken) is not Guid userId) return null;
        await using var gate = new NpgsqlCommand("SELECT id FROM users WHERE id=@user FOR UPDATE;", routing.Connection, routing.Transaction);
        gate.Parameters.AddWithValue("user", userId);
        if (await gate.ExecuteScalarAsync(cancellationToken) is null) return null;
        await using var command = new NpgsqlCommand($"""
            SELECT s.id,s.expires_at,s.revoked_at IS NOT NULL,
                {string.Join(", ", UserColumns.Split(", ").Select(c => "u." + c.Trim()))}
            FROM sessions s JOIN users u ON u.id=s.user_id
            WHERE s.token_hash=@hash AND s.user_id=@user FOR UPDATE OF s;
            """, routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("hash", tokenHash); command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var user = ReadUser(reader, 3);
        return new(user.Id, reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetBoolean(2), user.Status, user.EmailVerified);
    }
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
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var ownedTransaction = routing.Transaction is null
            ? await routing.Connection.BeginTransactionAsync(cancellationToken) : null;
        var connection = routing.Connection;
        var transaction = routing.Transaction ?? ownedTransaction!;
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
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
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
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
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
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE users
            SET password_hash = @password_hash,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @id;
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("password_hash", passwordHash);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateSessionAsync(
        SessionRecord session,
        CancellationToken cancellationToken = default)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO sessions(id, user_id, token_hash, created_at, expires_at)
            VALUES (@id, @user_id, @token_hash, @created_at, @expires_at);
            """,
            routing.Connection, routing.Transaction);
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
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE sessions
            SET revoked_at = COALESCE(revoked_at, @revoked_at)
            WHERE token_hash = @token_hash;
            """,
            routing.Connection, routing.Transaction);
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
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var ownedTransaction = routing.Transaction is null
            ? await routing.Connection.BeginTransactionAsync(cancellationToken) : null;
        var connection = routing.Connection;
        var transaction = routing.Transaction ?? ownedTransaction!;
        if (await FindTokenSubjectAsync("password_reset_tokens", tokenHash, usedAt, connection, transaction, cancellationToken) is null)
            return false;

        await using var lookup = new NpgsqlCommand(
            """
            SELECT t.user_id
            FROM password_reset_tokens t
            JOIN users u ON u.id = t.user_id
            WHERE t.token_hash = @token_hash
              AND t.used_at IS NULL
              AND t.revoked_at IS NULL
              AND t.expires_at > @used_at
              AND t.expires_at > clock_timestamp()
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
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
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
            WHERE token_hash = @token_hash AND used_at IS NULL AND revoked_at IS NULL
              AND expires_at > clock_timestamp();
            """,
            connection,
            transaction);
        consume.Parameters.AddWithValue("used_at", usedAt);
        consume.Parameters.AddWithValue("token_hash", tokenHash);
        if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1) return false;

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

        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
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
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var ownedTransaction = routing.Transaction is null
            ? await routing.Connection.BeginTransactionAsync(cancellationToken) : null;
        var connection = routing.Connection;
        var transaction = routing.Transaction ?? ownedTransaction!;
        if (await FindTokenSubjectAsync("email_verification_tokens", tokenHash, usedAt, connection, transaction, cancellationToken) is null)
            return false;

        await using var lookup = new NpgsqlCommand(
            """
            SELECT user_id
            FROM email_verification_tokens
            WHERE token_hash = @token_hash
              AND used_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > @used_at
              AND expires_at > clock_timestamp()
            FOR UPDATE;
            """,
            connection,
            transaction);
        lookup.Parameters.AddWithValue("token_hash", tokenHash);
        lookup.Parameters.AddWithValue("used_at", usedAt);
        var userIdObject = await lookup.ExecuteScalarAsync(cancellationToken);
        if (userIdObject is not Guid userId)
        {
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
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
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using var consume = new NpgsqlCommand(
            """
            UPDATE email_verification_tokens
            SET used_at = @used_at
            WHERE token_hash = @token_hash AND used_at IS NULL AND revoked_at IS NULL
              AND expires_at > clock_timestamp();
            """,
            connection,
            transaction);
        consume.Parameters.AddWithValue("used_at", usedAt);
        consume.Parameters.AddWithValue("token_hash", tokenHash);
        if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1) return false;

        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
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

    public async Task<IdentityOperation<IdentityEventPage>> ReadEventsAsync(Guid userId, long? after,
        CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasIdentityCommandScope)
            throw new InvalidOperationException("Identity replay requires a global identity command scope.");
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var head = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject',@subject,true);
            SELECT COALESCE((SELECT last_sequence FROM identity_event_streams WHERE user_id=@user),0);
            """, routing.Connection, routing.Transaction);
        head.Parameters.AddWithValue("subject", userId.ToString());
        head.Parameters.AddWithValue("user", userId);
        long latest;
        await using (var reader = await head.ExecuteReaderAsync(cancellationToken))
        {
            await reader.NextResultAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            latest = reader.GetInt64(0);
        }
        if (after is < 0 || after > latest)
            return IdentityOperation<IdentityEventPage>.Failure("invalid_identity_cursor");
        if (after is null) return IdentityOperation<IdentityEventPage>.Success(new(latest, latest, false, []));
        await using var command = new NpgsqlCommand("""
            SELECT event_id,sequence,event_type,actor_id,entity_id,entity_version,correlation_id,created_at
            FROM identity_events WHERE user_id=@user AND sequence>@after AND sequence<=@latest
            ORDER BY sequence LIMIT 100;
            """, routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("after", after.Value);
        command.Parameters.AddWithValue("latest", latest);
        var events = new List<IdentityDomainEvent>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                events.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetGuid(3),
                    null, null, "User", reader.GetGuid(4), reader.GetInt64(5),
                    new Dictionary<string,string>(), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        var cursor = after.Value;
        foreach (var item in events)
        {
            if (item.Sequence != cursor + 1) throw new InvalidOperationException("Identity event stream is not contiguous.");
            cursor = item.Sequence;
        }
        if (cursor < latest && events.Count < 100)
            throw new InvalidOperationException("Identity event stream is incomplete.");
        return IdentityOperation<IdentityEventPage>.Success(new(cursor, latest, cursor < latest, events));
    }

    public async Task AppendDomainEventAsync(Guid userId, string eventType, string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasIdentityCommandScope)
            throw new InvalidOperationException("Identity events require a global identity command scope.");
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        if (routing.Transaction is null)
            throw new InvalidOperationException("Identity events require an owning command transaction.");
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.identity_subject', @subject, true);
            INSERT INTO identity_event_streams(user_id) VALUES (@user) ON CONFLICT DO NOTHING;
            WITH allocated AS (
                UPDATE identity_event_streams SET last_sequence=last_sequence+1,updated_at=clock_timestamp()
                WHERE user_id=@user RETURNING last_sequence
            ) INSERT INTO identity_events(event_id,user_id,sequence,actor_id,event_type,entity_id,entity_version,correlation_id)
            SELECT @event,@user,a.last_sequence,@user,@type,@user,u.version,@correlation
            FROM allocated a JOIN users u ON u.id=@user;
            """, routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("subject", userId.ToString());
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("correlation", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        // A sign-in command locks the current account before checking its password/status.
        // Password reset/deactivation then serialize before session issuance or after its commit.
        var query = routing.Transaction is null ? sql : sql.TrimEnd(';') + " FOR UPDATE;";
        await using var command = new NpgsqlCommand(query, routing.Connection, routing.Transaction);
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
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var ownedTransaction = routing.Transaction is null
            ? await routing.Connection.BeginTransactionAsync(cancellationToken) : null;
        var connection = routing.Connection;
        var transaction = routing.Transaction ?? ownedTransaction!;
        await InsertSecurityTokenAsync(tableName, token, connection, transaction, cancellationToken);
        if (delivery is not null)
            await PublishIdentityDeliveryAsync(token, delivery, purpose, connection, transaction, cancellationToken);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
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
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        return await FindTokenSubjectAsync(tableName, tokenHash, now, routing.Connection, routing.Transaction, cancellationToken);
    }

    private static async Task<Guid?> FindTokenSubjectAsync(string tableName, string tokenHash, DateTimeOffset now,
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            SELECT u.id
            FROM users u JOIN {tableName} t ON t.user_id=u.id
            WHERE t.token_hash = @token_hash
              AND t.used_at IS NULL
              AND t.revoked_at IS NULL
              AND t.expires_at > @now
              AND t.expires_at > clock_timestamp()
              AND u.status <> 'DEACTIVATED'
              AND (@allow_suspended OR u.status <> 'SUSPENDED')
            """ + (transaction is null ? ";" : " FOR UPDATE OF u;"),
            connection, transaction);
        command.Parameters.AddWithValue("allow_suspended", tableName == "email_verification_tokens");
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
