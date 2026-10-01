using Npgsql;
using StrataAI.Application.Onboarding;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Onboarding;

internal sealed class PostgresInvitationStore(
    PostgresConnectionFactory connectionFactory) : IInvitationStore, IInvitationHistoryStore
{
    public async Task<IReadOnlyList<IssuedInvitation>> ListAsync(Guid organizationId, Guid? after, CancellationToken cancellationToken)
    {
        if (!connectionFactory.HasCommandScope(organizationId))
            throw new InvalidOperationException("Invitation history requires the authorized Organization transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT i.id,i.invited_email,i.target_surface,i.target_role,i.created_at,i.expires_at,i.accepted_at,i.revoked_at,
                CASE WHEN m.state='PENDING' AND j.state='FAILED' THEN 'RETRY_EXHAUSTED' ELSE m.state END
            FROM invitations i LEFT JOIN invitation_mail_intents m ON m.tenant_id=i.tenant_id AND m.invitation_id=i.id
            LEFT JOIN background_jobs j ON j.tenant_id=m.tenant_id AND j.id=m.job_id
            WHERE i.tenant_id=@tenant AND i.target_board_id IS NULL AND (@after::uuid IS NULL OR i.id>@after)
            ORDER BY i.id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var rows = new List<IssuedInvitation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), ParseSurface(reader.GetString(2)), reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        return rows;
    }
    public async Task<InvitationCreationReplay?> FindCreationReplayAsync(Guid organizationId, Guid actorId, Guid key,
        CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasCommandScope(organizationId)) throw new InvalidOperationException("Invitation replay requires the authorized Organization transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT i.id,i.tenant_id,i.invited_email,i.email_normalized,i.token_hash,i.target_surface,i.target_role,
                i.created_by_user_id,i.created_at,i.expires_at,i.accepted_at,i.revoked_at,i.accepted_by_user_id,i.target_board_id,i.target_board_role,
                r.fingerprint,r.expires_at<=clock_timestamp()
            FROM invitation_creation_replays r JOIN invitations i ON i.id=r.invitation_id AND i.tenant_id=r.tenant_id
            WHERE r.tenant_id=@tenant AND r.actor_id=@actor AND r.key_id=@key;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId); command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetString(15), reader.GetBoolean(16), ReadInvitation(reader)) : null;
    }

    public async Task SaveCreationReplayAsync(Guid organizationId, Guid actorId, Guid key, string fingerprint,
        Guid invitationId, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasCommandScope(organizationId)) throw new InvalidOperationException("Invitation replay requires the authorized Organization transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO invitation_creation_replays(tenant_id,actor_id,key_id,fingerprint,invitation_id)
            VALUES(@tenant,@actor,@key,@fingerprint,@invitation);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", organizationId); command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("key", key); command.Parameters.AddWithValue("fingerprint", fingerprint); command.Parameters.AddWithValue("invitation", invitationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InvitationRecord> CreateAsync(
        InvitationRecord invitation,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                invitation.OrganizationId,
                cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO invitations(
                id, tenant_id, invited_email, email_normalized,
                token_hash, target_surface, target_role,
                created_by_user_id, created_at, expires_at, target_board_id, target_board_role)
            VALUES (
                @id, @tenant_id, @invited_email, @email_normalized,
                @token_hash, @target_surface, @target_role,
                @created_by_user_id, @created_at, @expires_at, @target_board_id, @target_board_role)
            RETURNING id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
                created_by_user_id,created_at,expires_at,accepted_at,revoked_at,accepted_by_user_id,target_board_id,target_board_role;
            """,
            session.Connection,
            session.Transaction);

        command.Parameters.AddWithValue("id", invitation.Id);
        command.Parameters.AddWithValue(
            "tenant_id",
            invitation.OrganizationId);
        command.Parameters.AddWithValue(
            "invited_email",
            invitation.InvitedEmail);
        command.Parameters.AddWithValue(
            "email_normalized",
            invitation.EmailNormalized);
        command.Parameters.AddWithValue("token_hash", invitation.TokenHash);
        command.Parameters.AddWithValue(
            "target_surface",
            ToDatabaseSurface(invitation.Surface));
        command.Parameters.AddWithValue(
            "target_role",
            invitation.TargetRole);
        command.Parameters.AddWithValue(
            "created_by_user_id",
            invitation.CreatedByUserId);
        command.Parameters.AddWithValue("created_at", invitation.CreatedAt);
        command.Parameters.AddWithValue("expires_at", invitation.ExpiresAt);
        command.Parameters.AddWithValue("target_board_id", NpgsqlTypes.NpgsqlDbType.Uuid,
            (object?)invitation.BoardTarget?.BoardId ?? DBNull.Value);
        command.Parameters.AddWithValue("target_board_role", NpgsqlTypes.NpgsqlDbType.Text,
            invitation.BoardTarget is { } target ? target.Role switch {
                StrataAI.Application.WorkManagement.BoardRole.Admin => "ADMIN",
                StrataAI.Application.WorkManagement.BoardRole.Member => "MEMBER",
                _ => throw new ArgumentOutOfRangeException(nameof(invitation)),
            } : DBNull.Value);

        InvitationRecord persisted;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Created invitation was unavailable.");
            persisted = ReadInvitation(reader) with { OrganizationName = invitation.OrganizationName };
        }
        await session.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task<IReadOnlyList<PendingInvitation>> ListPendingForEmailAsync(
        string emailNormalized,
        DateTimeOffset now,
        Guid? after,
        CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasIdentityCommandScope) throw new InvalidOperationException("Invitation discovery requires a freshly authorized identity transaction.");
        await using var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                invitation_id,
                tenant_id,
                target_surface,
                target_role,
                expires_at, organization_name
            FROM invitation_routes
            WHERE email_normalized = @email_normalized
              AND target_board_id IS NULL
              AND accepted_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > clock_timestamp()
              AND (@after IS NULL OR invitation_id > @after)
            ORDER BY invitation_id LIMIT 51;
            """,
            routing.Connection, routing.Transaction);

        command.Parameters.AddWithValue(
            "email_normalized",
            emailNormalized);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);

        var result = new List<PendingInvitation>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new PendingInvitation(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    ParseSurface(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5)));
        }

        return result;
    }

    public async Task<InvitationRecord?> FindActiveByIdForEmailAsync(Guid invitationId, Guid actorUserId, string emailNormalized,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Guid? tenantId;
        await using (var routing = await connectionFactory.OpenRoutingSessionAsync(cancellationToken))
        {
            await using var route = new NpgsqlCommand("""
                SELECT tenant_id FROM invitation_routes WHERE invitation_id=@id AND email_normalized=@email
                  AND target_board_id IS NULL
                  AND (accepted_at IS NULL OR accepted_by_user_id=@actor) AND revoked_at IS NULL AND expires_at>clock_timestamp();
                """, routing.Connection, routing.Transaction);
            route.Parameters.AddWithValue("id", invitationId); route.Parameters.AddWithValue("email", emailNormalized);
            route.Parameters.AddWithValue("actor", actorUserId);
            tenantId = await route.ExecuteScalarAsync(cancellationToken) is Guid tenant ? tenant : null;
        }
        if (tenantId is null) return null;
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenantId.Value, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
              created_by_user_id,created_at,expires_at,accepted_at,revoked_at,accepted_by_user_id,target_board_id,target_board_role FROM invitations
            WHERE id=@id AND email_normalized=@email AND (accepted_at IS NULL OR accepted_by_user_id=@actor) AND revoked_at IS NULL
              AND expires_at>clock_timestamp();
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", invitationId); command.Parameters.AddWithValue("email", emailNormalized);
        command.Parameters.AddWithValue("actor", actorUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInvitation(reader) : null;
    }

    public async Task<InvitationRecord?> FindActiveByTokenHashAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await FindTenantRouteAsync(
            tokenHash,
            now,
            cancellationToken);

        if (tenantId is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                id, tenant_id, invited_email, email_normalized,
                token_hash, target_surface, target_role,
                created_by_user_id, created_at, expires_at,
                accepted_at, revoked_at, accepted_by_user_id, target_board_id, target_board_role
            FROM invitations
            WHERE token_hash = @token_hash
              AND accepted_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > clock_timestamp();
            """.TrimEnd().TrimEnd(';') + (connectionFactory.HasCommandScope(tenantId.Value) ? " FOR SHARE;" : ";"),
            session.Connection,
            session.Transaction);

        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("now", now);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadInvitation(reader)
            : null;
    }

    public async Task<InvitationAcceptStoreResult> AcceptAsync(
        string tokenHash,
        Guid userId,
        string emailNormalized,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await FindTenantRouteAsync(
            tokenHash,
            acceptedAt,
            cancellationToken);

        if (tenantId is null)
        {
            return new InvitationAcceptStoreResult(
                false,
                "invalid_or_expired_invitation",
                null);
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);

        InvitationRecord? invitation;
        await using (var lookup = new NpgsqlCommand(
            """
            SELECT
                id, tenant_id, invited_email, email_normalized,
                token_hash, target_surface, target_role,
                created_by_user_id, created_at, expires_at,
                accepted_at, revoked_at, accepted_by_user_id, target_board_id, target_board_role
            FROM invitations
            WHERE token_hash = @token_hash
              AND accepted_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > clock_timestamp()
            FOR UPDATE;
            """,
            session.Connection,
            session.Transaction))
        {
            lookup.Parameters.AddWithValue("token_hash", tokenHash);
            lookup.Parameters.AddWithValue("accepted_at", acceptedAt);

            await using var reader =
                await lookup.ExecuteReaderAsync(cancellationToken);

            invitation = await reader.ReadAsync(cancellationToken)
                ? ReadInvitation(reader)
                : null;
        }

        if (invitation is null || invitation.BoardTarget is not null ||
            invitation.EmailNormalized != emailNormalized)
        {
            return new InvitationAcceptStoreResult(
                false,
                "invalid_or_expired_invitation",
                null);
        }

        if (invitation.Surface == InvitationSurface.Internal)
        {
            await using var membership = new NpgsqlCommand(
                """
                INSERT INTO organization_members(
                    id, tenant_id, user_id, role, status,
                    created_at, updated_at, version)
                VALUES (
                    @id, @tenant_id, @user_id, @role, 'ACTIVE',
                    @accepted_at, @accepted_at, 1)
                ON CONFLICT (tenant_id, user_id)
                DO UPDATE SET
                    role = EXCLUDED.role,
                    status = 'ACTIVE',
                    updated_at = EXCLUDED.updated_at,
                    version = organization_members.version + 1;
                """,
                session.Connection,
                session.Transaction);

            membership.Parameters.AddWithValue("id", Guid.NewGuid());
            membership.Parameters.AddWithValue(
                "tenant_id",
                invitation.OrganizationId);
            membership.Parameters.AddWithValue("user_id", userId);
            membership.Parameters.AddWithValue(
                "role",
                invitation.TargetRole);
            membership.Parameters.AddWithValue("accepted_at", acceptedAt);
            await membership.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var portal = new NpgsqlCommand(
                """
                INSERT INTO portal_access(
                    id, tenant_id, user_id, status, relationship_type,
                    created_at, updated_at, version)
                VALUES (
                    @id, @tenant_id, @user_id, 'ACTIVE', @relationship_type,
                    @accepted_at, @accepted_at, 1)
                ON CONFLICT (tenant_id, user_id, relationship_type)
                DO UPDATE SET
                    status = 'ACTIVE',
                    updated_at = EXCLUDED.updated_at,
                    version = portal_access.version + 1;
                """,
                session.Connection,
                session.Transaction);

            portal.Parameters.AddWithValue("id", Guid.NewGuid());
            portal.Parameters.AddWithValue(
                "tenant_id",
                invitation.OrganizationId);
            portal.Parameters.AddWithValue("user_id", userId);
            portal.Parameters.AddWithValue(
                "relationship_type",
                invitation.TargetRole);
            portal.Parameters.AddWithValue("accepted_at", acceptedAt);
            await portal.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var consume = new NpgsqlCommand(
            """
            UPDATE invitations
            SET accepted_at = @accepted_at, accepted_by_user_id=@user_id
            WHERE id = @id
              AND accepted_at IS NULL
              AND revoked_at IS NULL AND expires_at>clock_timestamp();
            """,
            session.Connection,
            session.Transaction))
        {
            consume.Parameters.AddWithValue(
                "accepted_at",
                acceptedAt);
            consume.Parameters.AddWithValue("id", invitation.Id);
            consume.Parameters.AddWithValue("user_id", userId);
            if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return new InvitationAcceptStoreResult(
                    false,
                    "invalid_or_expired_invitation",
                    null);
            }
        }

        await session.CommitAsync(cancellationToken);

        return new InvitationAcceptStoreResult(
            true,
            null,
            invitation with { AcceptedAt = acceptedAt, AcceptedByUserId = userId });
    }

    public async Task<bool> RevokeAsync(
        Guid organizationId,
        Guid invitationId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE invitations
            SET revoked_at = @revoked_at
            WHERE id = @id
              AND tenant_id = @tenant_id
              AND accepted_at IS NULL
              AND revoked_at IS NULL;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("revoked_at", revokedAt);
        command.Parameters.AddWithValue("id", invitationId);
        command.Parameters.AddWithValue("tenant_id", organizationId);

        var changed =
            await command.ExecuteNonQueryAsync(cancellationToken) == 1;

        if (changed)
        {
            await session.CommitAsync(cancellationToken);
        }

        return changed;
    }

    private async Task<Guid?> FindTenantRouteAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT tenant_id
            FROM invitation_routes
            WHERE token_hash = @token_hash
              AND target_board_id IS NULL
              AND accepted_at IS NULL
              AND revoked_at IS NULL
              AND expires_at > clock_timestamp();
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("now", now);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid tenantId ? tenantId : null;
    }

    private static InvitationRecord ReadInvitation(
        NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ParseSurface(reader.GetString(5)),
            reader.GetString(6),
            reader.GetGuid(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            BoardTarget: reader.IsDBNull(13) ? null : new(reader.GetGuid(13), reader.GetString(14) switch {
                "ADMIN" => StrataAI.Application.WorkManagement.BoardRole.Admin,
                "MEMBER" => StrataAI.Application.WorkManagement.BoardRole.Member,
                _ => throw new InvalidOperationException("Unknown Board invitation role."),
            }));

    private static InvitationSurface ParseSurface(string surface) =>
        surface switch
        {
            "INTERNAL" => InvitationSurface.Internal,
            "PORTAL" => InvitationSurface.Portal,
            _ => throw new InvalidOperationException(
                $"Unknown invitation surface '{surface}'."),
        };

    private static string ToDatabaseSurface(InvitationSurface surface) =>
        surface switch
        {
            InvitationSurface.Internal => "INTERNAL",
            InvitationSurface.Portal => "PORTAL",
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
}
