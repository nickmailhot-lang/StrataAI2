using Npgsql;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.Organizations;

internal sealed class PostgresOrganizationStore(
    PostgresConnectionFactory connectionFactory) : IOrganizationStore
{
    public async Task<OrganizationRecord> CreateOrganizationAsync(
        Guid actorUserId,
        Guid organizationId,
        string name,
        string? description,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);

        await using (var organizationCommand = new NpgsqlCommand(
            """
            INSERT INTO organizations(
                id, name, description, owner_user_id, status,
                created_at, updated_at, version)
            VALUES (
                @id, @name, @description, @owner_user_id, 'ACTIVE',
                @created_at, @updated_at, 1);
            """,
            session.Connection,
            session.Transaction))
        {
            organizationCommand.Parameters.AddWithValue("id", organizationId);
            organizationCommand.Parameters.AddWithValue("name", name);
            organizationCommand.Parameters.AddWithValue(
                "description",
                description is null ? DBNull.Value : description);
            organizationCommand.Parameters.AddWithValue(
                "owner_user_id",
                actorUserId);
            organizationCommand.Parameters.AddWithValue("created_at", createdAt);
            organizationCommand.Parameters.AddWithValue("updated_at", createdAt);
            await organizationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var membershipCommand = new NpgsqlCommand(
            """
            INSERT INTO organization_members(
                id, tenant_id, user_id, role, status,
                created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @user_id, 'OWNER', 'ACTIVE',
                @created_at, @updated_at, 1);
            """,
            session.Connection,
            session.Transaction))
        {
            membershipCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            membershipCommand.Parameters.AddWithValue("tenant_id", organizationId);
            membershipCommand.Parameters.AddWithValue("user_id", actorUserId);
            membershipCommand.Parameters.AddWithValue("created_at", createdAt);
            membershipCommand.Parameters.AddWithValue("updated_at", createdAt);
            await membershipCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await session.CommitAsync(cancellationToken);

        return new OrganizationRecord(
            organizationId,
            name,
            description,
            null,
            actorUserId,
            OrganizationStatus.Active,
            createdAt,
            createdAt,
            1);
    }

    public async Task<IReadOnlyList<OrganizationSummary>> ListOrganizationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var routes = new List<(Guid TenantId, OrganizationRole Role)>();

        await using (var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(
            """
            SELECT tenant_id, role
            FROM user_organization_access
            WHERE user_id = @user_id
              AND status = 'ACTIVE'
            ORDER BY tenant_id;
            """,
            connection))
        {
            command.Parameters.AddWithValue("user_id", userId);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                routes.Add((
                    reader.GetGuid(0),
                    ParseRole(reader.GetString(1))));
            }
        }

        var organizations = new List<OrganizationSummary>(routes.Count);
        foreach (var route in routes)
        {
            var organization = await FindOrganizationAsync(
                route.TenantId,
                cancellationToken);

            if (organization is not null &&
                organization.Status != OrganizationStatus.Deleting)
            {
                organizations.Add(
                    new OrganizationSummary(organization, route.Role));
            }
        }

        return organizations
            .OrderBy(item => item.Organization.Name)
            .ToArray();
    }

    public async Task<OrganizationMembership?> FindMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                id, tenant_id, user_id, role, status,
                created_at, updated_at, version
            FROM organization_members
            WHERE tenant_id = @tenant_id
              AND user_id = @user_id;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("tenant_id", organizationId);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OrganizationMembership(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            ParseRole(reader.GetString(3)),
            reader.GetString(4) == "ACTIVE",
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt64(7));
    }

    public async Task<OrganizationRecord?> UpdateOrganizationAsync(
        Guid organizationId,
        string name,
        string? description,
        string? logoUrl,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE organizations
            SET name = @name,
                description = @description,
                logo_url = @logo_url,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @id
              AND version = @expected_version
              AND status <> 'DELETING'
            RETURNING
                id, name, description, logo_url, owner_user_id,
                status, created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", organizationId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue(
            "description",
            description is null ? DBNull.Value : description);
        command.Parameters.AddWithValue(
            "logo_url",
            logoUrl is null ? DBNull.Value : logoUrl);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadOrganization(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task AddOrRestoreMemberAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole role,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO organization_members(
                id, tenant_id, user_id, role, status,
                created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @user_id, @role, 'ACTIVE',
                @updated_at, @updated_at, 1)
            ON CONFLICT (tenant_id, user_id)
            DO UPDATE SET
                role = EXCLUDED.role,
                status = 'ACTIVE',
                updated_at = EXCLUDED.updated_at,
                version = organization_members.version + 1;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", organizationId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue(
            "role",
            role switch
            {
                OrganizationRole.Owner => "OWNER",
                OrganizationRole.Admin => "ADMIN",
                _ => "MEMBER",
            });
        command.Parameters.AddWithValue("updated_at", updatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    public async Task<OrganizationRemoveMemberResult> RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);

        string? targetRole;
        await using (var roleCommand = new NpgsqlCommand(
            """
            SELECT role
            FROM organization_members
            WHERE tenant_id = @tenant_id
              AND user_id = @user_id
              AND status = 'ACTIVE'
            FOR UPDATE;
            """,
            session.Connection,
            session.Transaction))
        {
            roleCommand.Parameters.AddWithValue("tenant_id", organizationId);
            roleCommand.Parameters.AddWithValue("user_id", userId);
            targetRole = (string?)await roleCommand.ExecuteScalarAsync(
                cancellationToken);
        }

        if (targetRole is null)
        {
            return OrganizationRemoveMemberResult.NotFound;
        }

        if (targetRole == "OWNER")
        {
            await using var countOwners = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM organization_members
                WHERE tenant_id = @tenant_id
                  AND role = 'OWNER'
                  AND status = 'ACTIVE';
                """,
                session.Connection,
                session.Transaction);
            countOwners.Parameters.AddWithValue("tenant_id", organizationId);
            var count = Convert.ToInt32(
                await countOwners.ExecuteScalarAsync(cancellationToken));

            if (count <= 1)
            {
                return OrganizationRemoveMemberResult.SoleOwner;
            }
        }

        await using var update = new NpgsqlCommand(
            """
            UPDATE organization_members
            SET status = 'REMOVED',
                updated_at = @updated_at,
                version = version + 1
            WHERE tenant_id = @tenant_id
              AND user_id = @user_id
              AND status = 'ACTIVE';
            """,
            session.Connection,
            session.Transaction);
        update.Parameters.AddWithValue("updated_at", updatedAt);
        update.Parameters.AddWithValue("tenant_id", organizationId);
        update.Parameters.AddWithValue("user_id", userId);
        await update.ExecuteNonQueryAsync(cancellationToken);

        await session.CommitAsync(cancellationToken);
        return OrganizationRemoveMemberResult.Removed;
    }

    public async Task<bool> MarkDeletingAsync(
        Guid organizationId,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE organizations
            SET status = 'DELETING',
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @id
              AND version = @expected_version
              AND status <> 'DELETING';
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", organizationId);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        var changed =
            await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (changed)
        {
            await session.CommitAsync(cancellationToken);
        }

        return changed;
    }

    public async Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_events(
                id, tenant_id, actor_id, event_type, entity_type,
                entity_id, correlation_id, safe_metadata, created_at)
            VALUES (
                @id, @tenant_id, @actor_id, @event_type, @entity_type,
                @entity_id, @correlation_id, '{}'::jsonb, now());
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", organizationId);
        command.Parameters.AddWithValue("actor_id", actorUserId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    private async Task<OrganizationRecord?> FindOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                id, name, description, logo_url, owner_user_id,
                status, created_at, updated_at, version
            FROM organizations
            WHERE id = @id;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", organizationId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadOrganization(reader)
            : null;
    }

    private static OrganizationRecord ReadOrganization(
        NpgsqlDataReader reader)
    {
        return new OrganizationRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetGuid(4),
            ParseStatus(reader.GetString(5)),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetInt64(8));
    }

    private static OrganizationRole ParseRole(string role) =>
        role switch
        {
            "OWNER" => OrganizationRole.Owner,
            "ADMIN" => OrganizationRole.Admin,
            "MEMBER" => OrganizationRole.Member,
            _ => throw new InvalidOperationException(
                $"Unknown Organization role '{role}'."),
        };

    private static OrganizationStatus ParseStatus(string status) =>
        status switch
        {
            "ACTIVE" => OrganizationStatus.Active,
            "ARCHIVED" => OrganizationStatus.Archived,
            "DELETING" => OrganizationStatus.Deleting,
            _ => throw new InvalidOperationException(
                $"Unknown Organization status '{status}'."),
        };
}
