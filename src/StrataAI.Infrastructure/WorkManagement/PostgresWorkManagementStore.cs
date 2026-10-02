using Npgsql;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Organizations;
using StrataAI.Infrastructure.Persistence;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class PostgresWorkManagementStore(
    PostgresConnectionFactory connectionFactory) : IWorkManagementStore
{
    public async Task<bool> AcquireCommandScopeAsync(Guid organizationId, Guid actorId,
        Guid? boardId, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasCommandScope(organizationId))
            throw new InvalidOperationException("Write authorization requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        async Task<object?> Lock(string sql)
        {
            await using var command = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("tenant", organizationId);
            command.Parameters.AddWithValue("actor", actorId);
            if (boardId is not null) command.Parameters.AddWithValue("board", boardId.Value);
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        // SHARE (rather than KEY SHARE) prevents status/role updates too. The
        // board gate serializes its commands before any child or event-stream lock.
        if (await Lock("SELECT id FROM organizations WHERE id=@tenant AND status='ACTIVE' FOR SHARE;") is null)
            return false;
        await Lock("SELECT id FROM organization_members WHERE tenant_id=@tenant AND user_id=@actor FOR SHARE;");
        if (boardId is not null)
        {
            if (await Lock("SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board FOR UPDATE;") is null)
                return false;
            await Lock("SELECT user_id FROM board_members WHERE tenant_id=@tenant AND board_id=@board AND user_id=@actor FOR SHARE;");
        }
        return true;
    }

    private static async Task<string> AllocateAppendRankAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid boardId, Guid? listId, CancellationToken cancellationToken, Guid? excludedCardId = null)
    {
        // Separate statements are intentional: after a concurrent creator releases
        // the parent lock, READ COMMITTED must obtain a fresh sibling snapshot.
        await using (var parent = new NpgsqlCommand(listId is null
            ? "SELECT id FROM boards WHERE tenant_id=@tenant AND id=@parent FOR UPDATE;"
            : "SELECT id FROM board_lists WHERE tenant_id=@tenant AND id=@parent FOR UPDATE;",
            connection, transaction))
        {
            parent.Parameters.AddWithValue("tenant", tenantId);
            parent.Parameters.AddWithValue("parent", listId ?? boardId);
            if (await parent.ExecuteScalarAsync(cancellationToken) is null)
                throw new InvalidOperationException("Rank parent was not found.");
        }

        await using var last = new NpgsqlCommand(listId is null
            ? "SELECT rank FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND lifecycle_state='ACTIVE' ORDER BY rank DESC LIMIT 1;"
            : "SELECT rank FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND lifecycle_state='ACTIVE' AND (@excluded IS NULL OR id<>@excluded) ORDER BY rank DESC LIMIT 1;",
            connection, transaction);
        last.Parameters.AddWithValue("tenant", tenantId);
        last.Parameters.AddWithValue("board", boardId);
        if (listId is not null)
        {
            last.Parameters.AddWithValue("list", listId.Value);
            last.Parameters.AddWithValue("excluded", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)excludedCardId ?? DBNull.Value);
        }
        return RankToken.After(await last.ExecuteScalarAsync(cancellationToken) as string);
    }

    private static async Task<string?> AllocateListPositionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid boardId, Guid movingId, Guid? beforeId, CancellationToken cancellationToken)
    {
        await using (var parent = new NpgsqlCommand("SELECT id FROM boards WHERE tenant_id=@tenant AND id=@board AND lifecycle_state='ACTIVE' FOR UPDATE;", connection, transaction))
        {
            parent.Parameters.AddWithValue("tenant", tenantId); parent.Parameters.AddWithValue("board", boardId);
            if (await parent.ExecuteScalarAsync(cancellationToken) is null) return null;
        }
        string? upper = null;
        if (beforeId is not null)
        {
            await using var anchor = new NpgsqlCommand("SELECT rank FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND id=@before AND id<>@moving AND lifecycle_state='ACTIVE';", connection, transaction);
            anchor.Parameters.AddWithValue("tenant", tenantId); anchor.Parameters.AddWithValue("board", boardId);
            anchor.Parameters.AddWithValue("before", beforeId.Value); anchor.Parameters.AddWithValue("moving", movingId);
            if (await anchor.ExecuteScalarAsync(cancellationToken) is not string value) return null;
            upper = value;
        }
        await using var previous = new NpgsqlCommand(beforeId is null
            ? "SELECT rank FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND id<>@moving AND lifecycle_state='ACTIVE' ORDER BY rank DESC,id DESC LIMIT 1;"
            : "SELECT rank FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND id<>@moving AND lifecycle_state='ACTIVE' AND (rank<@upper OR (rank=@upper AND id<@before)) ORDER BY rank DESC,id DESC LIMIT 1;", connection, transaction);
        previous.Parameters.AddWithValue("tenant", tenantId); previous.Parameters.AddWithValue("board", boardId); previous.Parameters.AddWithValue("moving", movingId);
        if (beforeId is not null) { previous.Parameters.AddWithValue("upper", upper!); previous.Parameters.AddWithValue("before", beforeId.Value); }
        var lower = await previous.ExecuteScalarAsync(cancellationToken) as string;
        if (upper is null) return RankToken.After(lower);
        if (lower == upper) throw new RankSpaceExhaustedException();
        return RankToken.Between(lower, upper);
    }

    private static async Task<string?> AllocateBeforeCardRankAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid boardId, Guid listId, Guid movingId, Guid beforeId, CancellationToken cancellationToken)
    {
        await using (var parent = new NpgsqlCommand("SELECT id FROM board_lists WHERE tenant_id=@tenant AND board_id=@board AND id=@list AND lifecycle_state='ACTIVE' FOR UPDATE;", connection, transaction))
        {
            parent.Parameters.AddWithValue("tenant", tenantId); parent.Parameters.AddWithValue("board", boardId); parent.Parameters.AddWithValue("list", listId);
            if (await parent.ExecuteScalarAsync(cancellationToken) is null) return null;
        }
        // Fresh statements after the lock ensure the anchor and predecessor are
        // current. Both predicates bind to the admitted tenant/Board/destination.
        await using var anchor = new NpgsqlCommand("SELECT rank FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND id=@before AND id<>@moving AND lifecycle_state='ACTIVE';", connection, transaction);
        anchor.Parameters.AddWithValue("tenant", tenantId); anchor.Parameters.AddWithValue("board", boardId); anchor.Parameters.AddWithValue("list", listId);
        anchor.Parameters.AddWithValue("before", beforeId); anchor.Parameters.AddWithValue("moving", movingId);
        if (await anchor.ExecuteScalarAsync(cancellationToken) is not string upper) return null;
        await using var previous = new NpgsqlCommand("SELECT rank FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND lifecycle_state='ACTIVE' AND id<>@moving AND (rank<@upper OR (rank=@upper AND id<@before)) ORDER BY rank DESC,id DESC LIMIT 1;", connection, transaction);
        previous.Parameters.AddWithValue("tenant", tenantId); previous.Parameters.AddWithValue("board", boardId); previous.Parameters.AddWithValue("list", listId);
        previous.Parameters.AddWithValue("moving", movingId); previous.Parameters.AddWithValue("upper", upper); previous.Parameters.AddWithValue("before", beforeId);
        var lower = await previous.ExecuteScalarAsync(cancellationToken) as string;
        if (lower == upper) throw new RankSpaceExhaustedException();
        return RankToken.Between(lower, upper);
    }

    public async Task<IReadOnlyList<OrganizationBoardSummary>> ListVisibleBoardsAsync(
        Guid organizationId,
        Guid userId,
        bool organizationAdministrator,
        CancellationToken cancellationToken = default)
    {
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT b.id, b.name, b.version
            FROM boards b
            WHERE b.tenant_id = @tenant_id
              AND b.lifecycle_state <> 'DELETED'
              AND (b.visibility <> 'PRIVATE' OR @organization_admin OR EXISTS (
                  SELECT 1 FROM board_members m
                  WHERE m.tenant_id = b.tenant_id AND m.board_id = b.id
                    AND m.user_id = @user_id AND m.status = 'ACTIVE'))
            ORDER BY b.name, b.id;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant_id", organizationId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("organization_admin", organizationAdministrator);
        var result = new List<OrganizationBoardSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new OrganizationBoardSummary(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2)));
        return result;
    }

    public async Task<BoardRecord> CreateBoardAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid boardId,
        string name,
        string? description,
        BoardVisibility visibility,
        string backgroundType,
        string? backgroundValue,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                organizationId,
                cancellationToken);

        await using (var boardCommand = new NpgsqlCommand(
            """
            INSERT INTO boards(
                id, tenant_id, name, description, visibility,
                background_type, background_value, lifecycle_state,
                created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @name, @description, @visibility,
                @background_type, @background_value, 'ACTIVE',
                @created_at, @updated_at, 1);
            """,
            session.Connection,
            session.Transaction))
        {
            boardCommand.Parameters.AddWithValue("id", boardId);
            boardCommand.Parameters.AddWithValue("tenant_id", organizationId);
            boardCommand.Parameters.AddWithValue("name", name);
            boardCommand.Parameters.AddWithValue(
                "description",
                description is null ? DBNull.Value : description);
            boardCommand.Parameters.AddWithValue(
                "visibility",
                ToDatabaseVisibility(visibility));
            boardCommand.Parameters.AddWithValue(
                "background_type",
                backgroundType);
            boardCommand.Parameters.AddWithValue(
                "background_value",
                backgroundValue is null ? DBNull.Value : backgroundValue);
            boardCommand.Parameters.AddWithValue("created_at", createdAt);
            boardCommand.Parameters.AddWithValue("updated_at", createdAt);
            await boardCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var memberCommand = new NpgsqlCommand(
            """
            INSERT INTO board_members(
                id, tenant_id, board_id, user_id, role, status,
                created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @board_id, @user_id, 'ADMIN', 'ACTIVE',
                @created_at, @updated_at, 1);
            """,
            session.Connection,
            session.Transaction))
        {
            memberCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            memberCommand.Parameters.AddWithValue("tenant_id", organizationId);
            memberCommand.Parameters.AddWithValue("board_id", boardId);
            memberCommand.Parameters.AddWithValue("user_id", actorUserId);
            memberCommand.Parameters.AddWithValue("created_at", createdAt);
            memberCommand.Parameters.AddWithValue("updated_at", createdAt);
            await memberCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await session.CommitAsync(cancellationToken);

        return new BoardRecord(
            boardId,
            organizationId,
            name,
            description,
            visibility,
            backgroundType,
            backgroundValue,
            BoardLifecycleState.Active,
            createdAt,
            createdAt,
            1);
    }

    public async Task<BoardRecord?> FindBoardAsync(
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        return tenantId.HasValue
            ? await FindBoardInTenantAsync(
                tenantId.Value,
                boardId,
                cancellationToken)
            : null;
    }

    public async Task<BoardMemberRecord?> FindBoardMemberAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
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
                board_id, user_id, role, status,
                created_at, updated_at, version
            FROM board_members
            WHERE board_id = @board_id
              AND user_id = @user_id;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadBoardMember(reader)
            : null;
    }

    public async Task<IReadOnlyList<ArchivedListEntry>> ListArchivedListsAsync(Guid boardId, Guid? after,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(boardId, cancellationToken);
        if (tenantId is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenantId.Value, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT l.id,l.tenant_id,l.board_id,l.name,l.rank,l.lifecycle_state,
                l.created_at,l.updated_at,l.version,
                (SELECT count(*) FROM cards c WHERE c.tenant_id=l.tenant_id AND c.board_id=l.board_id
                    AND c.list_id=l.id AND c.lifecycle_state<>'DELETED')
            FROM board_lists l
            WHERE l.tenant_id=@tenant AND l.board_id=@board AND l.lifecycle_state='ARCHIVED'
                AND (@after IS NULL OR l.id>@after)
            ORDER BY l.id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenantId.Value); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, after is null ? DBNull.Value : after.Value);
        var rows = new List<ArchivedListEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(ReadList(reader), reader.GetInt64(9)));
        return rows;
    }

    public async Task<IReadOnlyList<ArchivedCardEntry>> ListArchivedCardsAsync(Guid boardId, Guid? after,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(boardId, cancellationToken);
        if (tenantId is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(tenantId.Value, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,c.tenant_id,c.board_id,c.list_id,c.title,NULL::text,c.rank,c.lifecycle_state,
                c.created_at,c.updated_at,c.version,
                l.id,l.tenant_id,l.board_id,l.name,l.rank,l.lifecycle_state,l.created_at,l.updated_at,l.version
            FROM cards c JOIN board_lists l ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
            WHERE c.tenant_id=@tenant AND c.board_id=@board AND c.lifecycle_state='ARCHIVED'
                AND l.lifecycle_state<>'DELETED' AND (@after IS NULL OR c.id>@after)
            ORDER BY c.id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", tenantId.Value); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, after is null ? DBNull.Value : after.Value);
        var rows = new List<ArchivedCardEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new(ReadCard(reader),
            new(reader.GetGuid(11), reader.GetGuid(12), reader.GetGuid(13), reader.GetString(14), reader.GetString(15),
                ParseWorkLifecycle(reader.GetString(16)), reader.GetFieldValue<DateTimeOffset>(17),
                reader.GetFieldValue<DateTimeOffset>(18), reader.GetInt64(19))));
        return rows;
    }

    public async Task<BoardSnapshot?> GetSnapshotAsync(
        Guid boardId,
        Guid? userId,
        BoardAccess access,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);

        BoardRecord? board;
        await using (var boardCommand = new NpgsqlCommand(
            BoardSelect + " WHERE id = @board_id;",
            session.Connection,
            session.Transaction))
        {
            boardCommand.Parameters.AddWithValue("board_id", boardId);
            await using var reader =
                await boardCommand.ExecuteReaderAsync(cancellationToken);
            board = await reader.ReadAsync(cancellationToken)
                ? ReadBoard(reader)
                : null;
        }

        if (board is null)
        {
            return null;
        }

        var lists = new List<BoardListRecord>();
        await using (var listCommand = new NpgsqlCommand(
            """
            SELECT
                id, tenant_id, board_id, name, rank, lifecycle_state,
                created_at, updated_at, version
            FROM board_lists
            WHERE board_id = @board_id
              AND lifecycle_state = 'ACTIVE'
            ORDER BY rank, id;
            """,
            session.Connection,
            session.Transaction))
        {
            listCommand.Parameters.AddWithValue("board_id", boardId);
            await using var reader =
                await listCommand.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                lists.Add(ReadList(reader));
            }
        }

        var cardsByList = lists.ToDictionary(
            list => list.Id,
            _ => new List<CardRecord>());

        if (lists.Count > 0)
        {
            await using var cardCommand = new NpgsqlCommand(
                """
                SELECT
                    id, tenant_id, board_id, list_id, title, description,
                    rank, lifecycle_state, created_at, updated_at, version
                FROM cards
                WHERE board_id = @board_id
                  AND lifecycle_state = 'ACTIVE'
                ORDER BY list_id, rank, id;
                """,
                session.Connection,
                session.Transaction);
            cardCommand.Parameters.AddWithValue("board_id", boardId);

            await using var reader =
                await cardCommand.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var card = ReadCard(reader);
                if (cardsByList.TryGetValue(card.ListId, out var bucket))
                {
                    bucket.Add(card);
                }
            }
        }

        var starred = false;
        if (userId.HasValue)
        {
            await using var starCommand = new NpgsqlCommand(
                """
                SELECT starred
                FROM user_board_preferences
                WHERE board_id = @board_id
                  AND user_id = @user_id;
                """,
                session.Connection,
                session.Transaction);
            starCommand.Parameters.AddWithValue("board_id", boardId);
            starCommand.Parameters.AddWithValue("user_id", userId.Value);
            starred =
                await starCommand.ExecuteScalarAsync(cancellationToken) as bool?
                ?? false;
        }

        return new BoardSnapshot(
            board,
            lists.Select(
                    list =>
                        new BoardListSnapshot(
                            list,
                            cardsByList[list.Id]))
                .ToArray(),
            starred,
            access);
    }

    public Task<BoardRecord?> UpdateBoardAsync(
        Guid boardId,
        string name,
        string? description,
        string backgroundType,
        string? backgroundValue,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default) =>
        UpdateBoardInternalAsync(
            boardId,
            """
            name = @name,
            description = @description,
            background_type = @background_type,
            background_value = @background_value,
            """,
            command =>
            {
                command.Parameters.AddWithValue("name", name);
                command.Parameters.AddWithValue(
                    "description",
                    description is null ? DBNull.Value : description);
                command.Parameters.AddWithValue(
                    "background_type",
                    backgroundType);
                command.Parameters.AddWithValue(
                    "background_value",
                    backgroundValue is null ? DBNull.Value : backgroundValue);
            },
            expectedVersion,
            updatedAt,
            cancellationToken);

    public Task<BoardRecord?> SetBoardVisibilityAsync(
        Guid boardId,
        BoardVisibility visibility,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default) =>
        UpdateBoardInternalAsync(
            boardId,
            "visibility = @visibility,",
            command =>
                command.Parameters.AddWithValue(
                    "visibility",
                    ToDatabaseVisibility(visibility)),
            expectedVersion,
            updatedAt,
            cancellationToken);

    public async Task<BoardRecord?> SetBoardLifecycleAsync(
        Guid boardId,
        BoardLifecycleState expectedState,
        BoardLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE boards
            SET lifecycle_state = @next_state,
                archived_at = CASE
                    WHEN @next_state = 'ARCHIVED' THEN @updated_at
                    WHEN @next_state = 'ACTIVE' THEN NULL
                    ELSE archived_at
                END,
                deleted_at = CASE
                    WHEN @next_state = 'DELETED' THEN @updated_at
                    ELSE deleted_at
                END,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @board_id
              AND lifecycle_state = @expected_state
              AND version = @expected_version
            RETURNING {BoardColumns};
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue(
            "expected_state",
            ToDatabaseBoardLifecycle(expectedState));
        command.Parameters.AddWithValue(
            "next_state",
            ToDatabaseBoardLifecycle(nextState));
        command.Parameters.AddWithValue(
            "expected_version",
            expectedVersion);
        command.Parameters.AddWithValue("updated_at", updatedAt);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var board = ReadBoard(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return board;
    }

    public async Task SetStarAsync(
        Guid boardId,
        Guid userId,
        bool starred,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
        {
            return;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);

        if (starred)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO user_board_preferences(
                    tenant_id, board_id, user_id, starred, updated_at)
                VALUES (
                    @tenant_id, @board_id, @user_id, true, @updated_at)
                ON CONFLICT (board_id, user_id)
                DO UPDATE SET
                    starred = true,
                    updated_at = EXCLUDED.updated_at;
                """,
                session.Connection,
                session.Transaction);
            command.Parameters.AddWithValue("tenant_id", tenantId.Value);
            command.Parameters.AddWithValue("board_id", boardId);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("updated_at", updatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var command = new NpgsqlCommand(
                """
                DELETE FROM user_board_preferences
                WHERE board_id = @board_id
                  AND user_id = @user_id;
                """,
                session.Connection,
                session.Transaction);
            command.Parameters.AddWithValue("board_id", boardId);
            command.Parameters.AddWithValue("user_id", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await session.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BoardMemberRecord>> ListBoardMembersAsync(
        Guid boardId,
        CancellationToken cancellationToken = default, Guid? after = null, int? limit = null)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
        {
            return [];
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                board_id, user_id, role, status,
                created_at, updated_at, version
            FROM board_members
            WHERE board_id = @board_id
              AND status = 'ACTIVE'
              AND (@after::uuid IS NULL OR user_id > @after)
            ORDER BY user_id LIMIT @limit;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("board_id", boardId);

        command.Parameters.AddWithValue("after", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", NpgsqlTypes.NpgsqlDbType.Integer, (object?)limit ?? DBNull.Value);

        var result = new List<BoardMemberRecord>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadBoardMember(reader));
        }

        return result;
    }

    public async Task<BoardMemberRecord> UpsertBoardMemberAsync(
        Guid boardId,
        Guid userId,
        BoardRole role,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken)
            ?? throw new InvalidOperationException("Board was not found.");

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO board_members(
                id, tenant_id, board_id, user_id, role, status,
                created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @board_id, @user_id, @role, 'ACTIVE',
                @updated_at, @updated_at, 1)
            ON CONFLICT (board_id, user_id)
            DO UPDATE SET
                role = EXCLUDED.role,
                status = 'ACTIVE',
                updated_at = EXCLUDED.updated_at,
                version = board_members.version + 1
            RETURNING
                board_id, user_id, role, status,
                created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("role", ToDatabaseBoardRole(role));
        command.Parameters.AddWithValue("updated_at", updatedAt);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var result = ReadBoardMember(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> RemoveBoardMemberAsync(
        Guid boardId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
        {
            return false;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE board_members
            SET status = 'REMOVED',
                updated_at = @updated_at,
                version = version + 1
            WHERE board_id = @board_id
              AND user_id = @user_id
              AND status = 'ACTIVE';
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue("user_id", userId);

        var changed =
            await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (changed)
        {
            await session.CommitAsync(cancellationToken);
        }

        return changed;
    }

    public async Task<BoardListRecord> CreateListAsync(
        Guid boardId,
        Guid listId,
        string name,
        string? rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken)
            ?? throw new InvalidOperationException("Board was not found.");

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId,
                cancellationToken);
        rank ??= await AllocateAppendRankAsync(session.Connection, session.Transaction,
            tenantId, boardId, null, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO board_lists(
                id, tenant_id, board_id, name, rank, lifecycle_state,
                created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @board_id, @name, @rank, 'ACTIVE',
                @created_at, @updated_at, 1);
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", listId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("rank", rank);
        command.Parameters.AddWithValue("created_at", createdAt);
        command.Parameters.AddWithValue("updated_at", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);

        return new BoardListRecord(
            listId,
            tenantId,
            boardId,
            name,
            rank,
            WorkItemLifecycleState.Active,
            createdAt,
            createdAt,
            1);
    }

    public async Task<BoardListRecord?> FindListAsync(
        Guid listId,
        CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        var route = await ResolveListRouteAsync(
            listId,
            cancellationToken, includeDeleted);

        if (route is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.Value.TenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                id, tenant_id, board_id, name, rank, lifecycle_state,
                created_at, updated_at, version
            FROM board_lists
            WHERE id = @list_id;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("list_id", listId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadList(reader)
            : null;
    }

    public async Task<long> CountContainedCardsAsync(Guid listId, CancellationToken cancellationToken = default)
    {
        var route = await ResolveListRouteAsync(listId, cancellationToken);
        if (route is null) return 0;
        await using var session = await connectionFactory.OpenTenantSessionAsync(route.Value.TenantId, cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM cards WHERE tenant_id=@tenant AND board_id=@board AND list_id=@list AND lifecycle_state<>'DELETED';", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", route.Value.TenantId); command.Parameters.AddWithValue("board", route.Value.BoardId);
        command.Parameters.AddWithValue("list", listId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<BoardListRecord?> UpdateListAsync(
        Guid listId,
        string name,
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? beforeListId = null, bool moveToEnd = false)
    {
        var route = await ResolveListRouteAsync(
            listId,
            cancellationToken);

        if (route is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.Value.TenantId,
                cancellationToken);
        if (beforeListId is not null || moveToEnd)
        {
            var allocated = await AllocateListPositionAsync(session.Connection, session.Transaction,
                route.Value.TenantId, route.Value.BoardId, listId, beforeListId, cancellationToken);
            if (allocated is null) return null;
            rank = allocated;
        }
        await using var command = new NpgsqlCommand(
            """
            UPDATE board_lists
            SET name = @name,
                rank = @rank,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @list_id
              AND tenant_id = @tenant_id
              AND board_id = @board_id
              AND version = @expected_version
              AND lifecycle_state = 'ACTIVE'
            RETURNING
                id, tenant_id, board_id, name, rank, lifecycle_state,
                created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("tenant_id", route.Value.TenantId);
        command.Parameters.AddWithValue("board_id", route.Value.BoardId);
        command.Parameters.AddWithValue("rank", rank);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("list_id", listId);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadList(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<BoardListRecord?> SetListLifecycleAsync(
        Guid listId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var route = await ResolveListRouteAsync(
            listId,
            cancellationToken);

        if (route is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.Value.TenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE board_lists
            SET lifecycle_state = @next_state,
                archived_at = CASE
                    WHEN @next_state = 'ARCHIVED' THEN @updated_at
                    WHEN @next_state = 'ACTIVE' THEN NULL
                    ELSE archived_at
                END,
                deleted_at = CASE
                    WHEN @next_state = 'DELETED' THEN @updated_at
                    ELSE deleted_at
                END,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @list_id
              AND lifecycle_state = @expected_state
              AND version = @expected_version
            RETURNING
                id, tenant_id, board_id, name, rank, lifecycle_state,
                created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("list_id", listId);
        command.Parameters.AddWithValue(
            "expected_state",
            ToDatabaseWorkLifecycle(expectedState));
        command.Parameters.AddWithValue(
            "next_state",
            ToDatabaseWorkLifecycle(nextState));
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        command.Parameters.AddWithValue("updated_at", updatedAt);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadList(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CardRecord> CreateCardAsync(
        Guid listId,
        Guid cardId,
        string title,
        string? description,
        string? rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var route = await ResolveListRouteAsync(
            listId,
            cancellationToken)
            ?? throw new InvalidOperationException("List was not found.");

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.TenantId,
                cancellationToken);
        rank ??= await AllocateAppendRankAsync(session.Connection, session.Transaction,
            route.TenantId, route.BoardId, listId, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO cards(
                id, tenant_id, board_id, list_id, title, description,
                rank, lifecycle_state, created_at, updated_at, version)
            VALUES (
                @id, @tenant_id, @board_id, @list_id, @title, @description,
                @rank, 'ACTIVE', @created_at, @updated_at, 1);
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("id", cardId);
        command.Parameters.AddWithValue("tenant_id", route.TenantId);
        command.Parameters.AddWithValue("board_id", route.BoardId);
        command.Parameters.AddWithValue("list_id", listId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue(
            "description",
            description is null ? DBNull.Value : description);
        command.Parameters.AddWithValue("rank", rank);
        command.Parameters.AddWithValue("created_at", createdAt);
        command.Parameters.AddWithValue("updated_at", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);

        return new CardRecord(
            cardId,
            route.TenantId,
            route.BoardId,
            listId,
            title,
            description,
            rank,
            WorkItemLifecycleState.Active,
            createdAt,
            createdAt,
            1);
    }

    public async Task<CardRecord?> FindCardAsync(
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        var route = await ResolveCardRouteAsync(
            cardId,
            cancellationToken);

        if (route is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.Value.TenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                id, tenant_id, board_id, list_id, title, description,
                rank, lifecycle_state, created_at, updated_at, version
            FROM cards
            WHERE id = @card_id;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("card_id", cardId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCard(reader)
            : null;
    }

    public async Task<CardRecord?> UpdateCardAsync(
        Guid cardId,
        string title,
        string? description,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var route = await ResolveCardRouteAsync(
            cardId,
            cancellationToken);

        if (route is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.Value.TenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE cards
            SET title = @title,
                description = @description,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @card_id
              AND version = @expected_version
              AND lifecycle_state = 'ACTIVE'
            RETURNING
                id, tenant_id, board_id, list_id, title, description,
                rank, lifecycle_state, created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue(
            "description",
            description is null ? DBNull.Value : description);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("card_id", cardId);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadCard(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CardRecord?> MoveCardAsync(
        Guid cardId,
        Guid destinationListId,
        string? rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? beforeCardId = null)
    {
        var cardRoute = await ResolveCardRouteAsync(
            cardId,
            cancellationToken);
        var listRoute = await ResolveListRouteAsync(
            destinationListId,
            cancellationToken);

        if (cardRoute is null ||
            listRoute is null ||
            cardRoute.Value.TenantId != listRoute.Value.TenantId ||
            cardRoute.Value.BoardId != listRoute.Value.BoardId)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                cardRoute.Value.TenantId,
                cancellationToken);
        if (beforeCardId is not null)
        {
            rank = await AllocateBeforeCardRankAsync(session.Connection, session.Transaction,
                cardRoute.Value.TenantId, cardRoute.Value.BoardId, destinationListId, cardId, beforeCardId.Value, cancellationToken);
            if (rank is null) return null;
        }
        else rank ??= await AllocateAppendRankAsync(session.Connection, session.Transaction,
            cardRoute.Value.TenantId, cardRoute.Value.BoardId, destinationListId, cancellationToken, cardId);
        await using var command = new NpgsqlCommand(
            """
            UPDATE cards
            SET list_id = @destination_list_id,
                rank = @rank,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @card_id
              AND tenant_id = @tenant_id
              AND board_id = @board_id
              AND list_id = @source_list_id
              AND version = @expected_version
              AND lifecycle_state = 'ACTIVE'
            RETURNING
                id, tenant_id, board_id, list_id, title, description,
                rank, lifecycle_state, created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue(
            "destination_list_id",
            destinationListId);
        command.Parameters.AddWithValue("tenant_id", cardRoute.Value.TenantId);
        command.Parameters.AddWithValue("board_id", cardRoute.Value.BoardId);
        command.Parameters.AddWithValue("source_list_id", cardRoute.Value.ListId);
        command.Parameters.AddWithValue("rank", rank);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("card_id", cardId);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadCard(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CardRecord?> SetCardLifecycleAsync(
        Guid cardId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var route = await ResolveCardRouteAsync(
            cardId,
            cancellationToken);

        if (route is null)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                route.Value.TenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE cards
            SET lifecycle_state = @next_state,
                archived_at = CASE
                    WHEN @next_state = 'ARCHIVED' THEN @updated_at
                    WHEN @next_state = 'ACTIVE' THEN NULL
                    ELSE archived_at
                END,
                deleted_at = CASE
                    WHEN @next_state = 'DELETED' THEN @updated_at
                    ELSE deleted_at
                END,
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @card_id
              AND lifecycle_state = @expected_state
              AND version = @expected_version
            RETURNING
                id, tenant_id, board_id, list_id, title, description,
                rank, lifecycle_state, created_at, updated_at, version;
            """,
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("card_id", cardId);
        command.Parameters.AddWithValue(
            "expected_state",
            ToDatabaseWorkLifecycle(expectedState));
        command.Parameters.AddWithValue(
            "next_state",
            ToDatabaseWorkLifecycle(nextState));
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        command.Parameters.AddWithValue("updated_at", updatedAt);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadCard(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
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

    private async Task<BoardRecord?> UpdateBoardInternalAsync(
        Guid boardId,
        string assignments,
        Action<NpgsqlCommand> addParameters,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var tenantId = await ResolveBoardTenantAsync(
            boardId,
            cancellationToken);

        if (!tenantId.HasValue)
        {
            return null;
        }

        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId.Value,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE boards
            SET {assignments}
                updated_at = @updated_at,
                version = version + 1
            WHERE id = @board_id
              AND version = @expected_version
              AND lifecycle_state <> 'DELETED'
            RETURNING {BoardColumns};
            """,
            session.Connection,
            session.Transaction);

        addParameters(command);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        command.Parameters.AddWithValue("board_id", boardId);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = ReadBoard(reader);
        await reader.DisposeAsync();
        await session.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<BoardRecord?> FindBoardInTenantAsync(
        Guid tenantId,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        await using var session =
            await connectionFactory.OpenTenantSessionAsync(
                tenantId,
                cancellationToken);
        await using var command = new NpgsqlCommand(
            BoardSelect + " WHERE id = @board_id;",
            session.Connection,
            session.Transaction);
        command.Parameters.AddWithValue("board_id", boardId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadBoard(reader)
            : null;
    }

    private async Task<Guid?> ResolveBoardTenantAsync(
        Guid boardId,
        CancellationToken cancellationToken)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await routing.SetLookupAsync(RoutingLookup.Board, boardId.ToString(), cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT tenant_id
            FROM board_routes
            WHERE board_id = @board_id
              AND lifecycle_state <> 'DELETED';
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("board_id", boardId);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid tenantId ? tenantId : null;
    }

    private async Task<(Guid TenantId, Guid BoardId)?> ResolveListRouteAsync(
        Guid listId,
        CancellationToken cancellationToken, bool includeDeleted = false)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await routing.SetLookupAsync(RoutingLookup.List, listId.ToString(), cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT tenant_id, board_id
            FROM list_routes
            WHERE list_id = @list_id
              AND (@include_deleted OR lifecycle_state <> 'DELETED');
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("list_id", listId);
        command.Parameters.AddWithValue("include_deleted", includeDeleted);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1))
            : null;
    }

    private async Task<(Guid TenantId, Guid BoardId, Guid ListId)?> ResolveCardRouteAsync(
        Guid cardId,
        CancellationToken cancellationToken)
    {
        await using var routing =
            await connectionFactory.OpenRoutingSessionAsync(cancellationToken);
        await routing.SetLookupAsync(RoutingLookup.Card, cardId.ToString(), cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT tenant_id, board_id, list_id
            FROM card_routes
            WHERE card_id = @card_id
              AND lifecycle_state <> 'DELETED';
            """,
            routing.Connection, routing.Transaction);
        command.Parameters.AddWithValue("card_id", cardId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2))
            : null;
    }

    private const string BoardColumns = """
        id, tenant_id, name, description, visibility,
        background_type, background_value, lifecycle_state,
        created_at, updated_at, version
        """;

    private const string BoardSelect = "SELECT " + BoardColumns + " FROM boards";

    private static BoardRecord ReadBoard(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            ParseVisibility(reader.GetString(4)),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            ParseBoardLifecycle(reader.GetString(7)),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetInt64(10));

    private static BoardListRecord ReadList(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            ParseWorkLifecycle(reader.GetString(5)),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetInt64(8));

    private static CardRecord ReadCard(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            ParseWorkLifecycle(reader.GetString(7)),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetInt64(10));

    private static BoardMemberRecord ReadBoardMember(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ParseBoardRole(reader.GetString(2)),
            reader.GetString(3) == "ACTIVE",
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetInt64(6));

    private static string ToDatabaseVisibility(BoardVisibility visibility) =>
        visibility switch
        {
            BoardVisibility.Private => "PRIVATE",
            BoardVisibility.Organization => "ORGANIZATION",
            BoardVisibility.Public => "PUBLIC",
            _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
        };

    private static BoardVisibility ParseVisibility(string visibility) =>
        visibility switch
        {
            "PRIVATE" => BoardVisibility.Private,
            "ORGANIZATION" => BoardVisibility.Organization,
            "PUBLIC" => BoardVisibility.Public,
            _ => throw new InvalidOperationException(
                $"Unknown board visibility '{visibility}'."),
        };

    private static string ToDatabaseBoardLifecycle(BoardLifecycleState state) =>
        state switch
        {
            BoardLifecycleState.Active => "ACTIVE",
            BoardLifecycleState.Archived => "ARCHIVED",
            BoardLifecycleState.Deleted => "DELETED",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static BoardLifecycleState ParseBoardLifecycle(string state) =>
        state switch
        {
            "ACTIVE" => BoardLifecycleState.Active,
            "ARCHIVED" => BoardLifecycleState.Archived,
            "DELETED" => BoardLifecycleState.Deleted,
            _ => throw new InvalidOperationException(
                $"Unknown board lifecycle '{state}'."),
        };

    private static string ToDatabaseWorkLifecycle(WorkItemLifecycleState state) =>
        state switch
        {
            WorkItemLifecycleState.Active => "ACTIVE",
            WorkItemLifecycleState.Archived => "ARCHIVED",
            WorkItemLifecycleState.Deleted => "DELETED",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static WorkItemLifecycleState ParseWorkLifecycle(string state) =>
        state switch
        {
            "ACTIVE" => WorkItemLifecycleState.Active,
            "ARCHIVED" => WorkItemLifecycleState.Archived,
            "DELETED" => WorkItemLifecycleState.Deleted,
            _ => throw new InvalidOperationException(
                $"Unknown item lifecycle '{state}'."),
        };

    private static string ToDatabaseBoardRole(BoardRole role) =>
        role switch
        {
            BoardRole.Admin => "ADMIN",
            BoardRole.Member => "MEMBER",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static BoardRole ParseBoardRole(string role) =>
        role switch
        {
            "ADMIN" => BoardRole.Admin,
            "MEMBER" => BoardRole.Member,
            _ => throw new InvalidOperationException(
                $"Unknown board role '{role}'."),
        };
}
