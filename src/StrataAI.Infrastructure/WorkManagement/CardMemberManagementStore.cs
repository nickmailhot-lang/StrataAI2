using Npgsql;
using StrataAI.Application.Identity;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardRecord>> RemoveOrganizationCardMemberAssignmentsAsync(Guid organizationId, Guid userId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.HasCommandScope(organizationId)) throw new InvalidOperationException("Organization assignment cleanup requires its owning transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(organizationId, cancellationToken);
        // The Organization command holds the parent FOR UPDATE, excluding work
        // commands before their Board/Card/event locks. Preserve other tenants.
        await using var query = new NpgsqlCommand("""
            WITH removed AS (
                DELETE FROM card_members WHERE tenant_id=@tenant AND user_id=@user RETURNING card_id,board_id
            ), changed AS (
                UPDATE cards c SET version=c.version+1,updated_at=@now
                WHERE c.tenant_id=@tenant AND EXISTS(SELECT 1 FROM removed r WHERE r.card_id=c.id AND r.board_id=c.board_id)
                RETURNING c.id,c.tenant_id,c.board_id,c.list_id,c.title,c.description,c.rank,c.lifecycle_state,c.created_at,c.updated_at,c.version,c.start_at,c.due_at,c.due_timezone,c.due_has_time,c.due_complete,c.archived_at,c.deleted_at
            ) SELECT * FROM changed ORDER BY board_id,id;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", organizationId); query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("now", now);
        var result = new List<CardRecord>(); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCard(reader));
        return result;
    }
    public async Task<IReadOnlyList<CardRecord>> RemoveBoardCardMemberAssignmentsAsync(Guid boardId, Guid userId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken); if (board is null) return [];
        if (!connectionFactory.HasCommandScope(board.OrganizationId)) throw new InvalidOperationException("Assignment departure cleanup requires the owning Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            WITH removed AS (
                DELETE FROM card_members WHERE tenant_id=@tenant AND board_id=@board AND user_id=@user RETURNING card_id
            ), changed AS (
                UPDATE cards c SET version=c.version+1,updated_at=@now
                WHERE c.tenant_id=@tenant AND c.board_id=@board AND c.id IN (SELECT card_id FROM removed)
                RETURNING c.id,c.tenant_id,c.board_id,c.list_id,c.title,c.description,c.rank,c.lifecycle_state,c.created_at,c.updated_at,c.version,c.start_at,c.due_at,c.due_timezone,c.due_has_time,c.due_complete,c.archived_at,c.deleted_at
            ) SELECT * FROM changed ORDER BY id;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", board.OrganizationId); query.Parameters.AddWithValue("board", boardId);
        query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("now", now);
        var result = new List<CardRecord>(); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCard(reader));
        return result;
    }
    public async Task<bool> IsAssignableBoardMemberAsync(Guid boardId, Guid userId, bool requireVerifiedEmail, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken); if (board is null) return false;
        if (!connectionFactory.HasCommandScope(board.OrganizationId)) throw new InvalidOperationException("Assignment eligibility requires the owning Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT m.user_id FROM board_members m
            JOIN organization_members o ON o.tenant_id=m.tenant_id AND o.user_id=m.user_id
            JOIN users u ON u.id=m.user_id
            WHERE m.tenant_id=@tenant AND m.board_id=@board AND m.user_id=@user
                AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE'
                AND (NOT @verified OR u.email_verified) FOR SHARE OF m,o,u;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", board.OrganizationId); query.Parameters.AddWithValue("board", boardId);
        query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        return await query.ExecuteScalarAsync(cancellationToken) is Guid;
    }
    public async Task<CardMemberChange?> SetCardMemberAsync(Guid cardId, Guid userId, Guid actorId, bool assigned,
        long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var card = await FindCardAsync(cardId, cancellationToken); if (card is null) return null;
        if (!connectionFactory.HasCommandScope(card.OrganizationId)) throw new InvalidOperationException("Card assignment requires the owning command transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(card.OrganizationId, cancellationToken);
        NpgsqlCommand Query(string sql)
        {
            var query = new NpgsqlCommand(sql, session.Connection, session.Transaction);
            query.Parameters.AddWithValue("tenant", card.OrganizationId); query.Parameters.AddWithValue("board", card.BoardId);
            query.Parameters.AddWithValue("card", cardId); query.Parameters.AddWithValue("user", userId); query.Parameters.AddWithValue("actor", actorId);
            query.Parameters.AddWithValue("version", version); query.Parameters.AddWithValue("now", now); return query;
        }
        await using (var current = Query("SELECT version FROM cards WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND lifecycle_state='ACTIVE' FOR UPDATE;"))
            if (await current.ExecuteScalarAsync(cancellationToken) is not long revision || revision != version) return null;
        int count;
        await using (var change = Query(assigned
            ? "INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by,created_at,updated_at) VALUES(@tenant,@board,@card,@user,@actor,@now,@now) ON CONFLICT(tenant_id,card_id,user_id) DO NOTHING;"
            : "DELETE FROM card_members WHERE tenant_id=@tenant AND board_id=@board AND card_id=@card AND user_id=@user;"))
            count = await change.ExecuteNonQueryAsync(cancellationToken);
        if (count > 0)
        {
            await using var update = Query("UPDATE cards SET version=version+1,updated_at=@now WHERE tenant_id=@tenant AND board_id=@board AND id=@card AND version=@version RETURNING id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,version, start_at, due_at, due_timezone, due_has_time, due_complete, archived_at, deleted_at;");
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Locked Card assignment revision changed unexpectedly.");
            card = ReadCard(reader);
        }
        return new(card, userId, assigned, count > 0);
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    private readonly Dictionary<(Guid CardId, Guid UserId), (Guid AssignedBy, DateTimeOffset AssignedAt)> _cardMembers = [];
    public Task<IReadOnlyList<CardRecord>> RemoveOrganizationCardMemberAssignmentsAsync(Guid organizationId, Guid userId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var cards = _cardMembers.Keys.Where(k => k.UserId == userId && _cards.TryGetValue(k.CardId, out var c) && c.OrganizationId == organizationId)
                .Select(k => _cards[k.CardId]).OrderBy(c => c.BoardId).ThenBy(c => c.Id).ToArray();
            var result = new List<CardRecord>();
            foreach (var card in cards)
            {
                _cardMembers.Remove((card.Id, userId)); var updated = card with { Version = card.Version + 1, UpdatedAt = now };
                _cards[card.Id] = updated; result.Add(updated);
            }
            return Task.FromResult<IReadOnlyList<CardRecord>>(result);
        }
    }
    public Task<IReadOnlyList<CardRecord>> RemoveBoardCardMemberAssignmentsAsync(Guid boardId, Guid userId,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var cards = _cardMembers.Keys.Where(k => k.UserId == userId && _cards.TryGetValue(k.CardId, out var c) && c.BoardId == boardId)
                .Select(k => _cards[k.CardId]).OrderBy(c => c.Id).ToArray();
            var result = new List<CardRecord>();
            foreach (var card in cards)
            {
                _cardMembers.Remove((card.Id, userId)); var updated = card with { Version = card.Version + 1, UpdatedAt = now };
                _cards[card.Id] = updated; result.Add(updated);
            }
            return Task.FromResult<IReadOnlyList<CardRecord>>(result);
        }
    }
    public async Task<bool> IsAssignableBoardMemberAsync(Guid boardId, Guid userId, bool requireVerifiedEmail, CancellationToken cancellationToken = default)
    {
        BoardRecord? board;
        lock (_sync) if (!_boards.TryGetValue(boardId, out board) || !_members.TryGetValue((boardId, userId), out var member) || !member.Active) return false;
        var profiles = await organizations.ListActiveMembersAsync(board.OrganizationId, null, cancellationToken, userId);
        lock (_sync) return profiles.Any(p => p.UserId == userId && p.AccountStatus == AccountStatus.Active && (!requireVerifiedEmail || p.EmailVerified))
            && _members.TryGetValue((boardId, userId), out var current) && current.Active;
    }
    public Task<CardMemberChange?> SetCardMemberAsync(Guid cardId, Guid userId, Guid actorId, bool assigned,
        long version, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) || card.LifecycleState != WorkItemLifecycleState.Active || card.Version != version)
                return Task.FromResult<CardMemberChange?>(null);
            var changed = assigned ? _cardMembers.TryAdd((cardId, userId), (actorId, now)) : _cardMembers.Remove((cardId, userId));
            if (changed) _cards[cardId] = card = card with { Version = card.Version + 1, UpdatedAt = now };
            return Task.FromResult<CardMemberChange?>(new(card, userId, assigned, changed));
        }
    }
}
