using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;
using StrataAI.Application.Identity;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardRecord>> FilterBoardCardsAsync(Guid boardId, BoardCardFilter filter, Guid? after, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken);
        if (board is null) return [];
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,c.tenant_id,c.board_id,c.list_id,c.title,c.description,c.rank,c.lifecycle_state,c.created_at,c.updated_at,c.version,c.start_at,c.due_at,c.due_timezone,c.due_has_time,c.due_complete
            FROM cards c JOIN board_lists parent ON parent.id=c.list_id AND parent.board_id=c.board_id AND parent.tenant_id=c.tenant_id
            CROSS JOIN LATERAL (
              SELECT count(*) AS hits FROM card_labels a JOIN board_labels l
                ON l.id=a.label_id AND l.board_id=a.board_id AND l.tenant_id=a.tenant_id
              WHERE a.tenant_id=c.tenant_id AND a.board_id=c.board_id AND a.card_id=c.id
                AND l.status='ACTIVE' AND l.id=ANY(@labels)
            ) matches
            CROSS JOIN LATERAL (
              SELECT count(*) AS hits FROM card_members a
              JOIN board_members m ON m.tenant_id=a.tenant_id AND m.board_id=a.board_id AND m.user_id=a.user_id
              JOIN organization_members o ON o.tenant_id=a.tenant_id AND o.user_id=a.user_id JOIN users u ON u.id=a.user_id
              WHERE a.tenant_id=c.tenant_id AND a.board_id=c.board_id AND a.card_id=c.id AND a.user_id=ANY(@members)
                AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT @verified OR u.email_verified)
            ) assignees
            CROSS JOIN LATERAL (SELECT CASE @due
              WHEN 'none' THEN c.due_at IS NULL
              WHEN 'overdue' THEN c.due_at IS NOT NULL AND NOT c.due_complete AND c.due_at<@now
              WHEN 'upcoming' THEN c.due_at IS NOT NULL AND NOT c.due_complete AND c.due_at>=@now
              ELSE false END AS hit) deadline
            WHERE c.tenant_id=@tenant AND c.board_id=@board AND c.lifecycle_state='ACTIVE' AND parent.lifecycle_state='ACTIVE'
              AND (@after IS NULL OR c.id>@after)
              AND (
                (@keyword='' AND cardinality(@labels)=0 AND cardinality(@members)=0 AND @completion IS NULL AND @due='all' AND @recent IS NULL)
                OR (@all AND (@keyword='' OR strpos(lower(c.title),lower(@keyword))>0 OR strpos(lower(coalesce(c.description,'')),lower(@keyword))>0)
                    AND matches.hits=cardinality(@labels) AND assignees.hits=cardinality(@members)
                    AND (@completion IS NULL OR c.due_complete=@completion) AND (@due='all' OR deadline.hit)
                    AND (@recent IS NULL OR c.updated_at>=@recent))
                OR (NOT @all AND ((@keyword<>'' AND (strpos(lower(c.title),lower(@keyword))>0 OR strpos(lower(coalesce(c.description,'')),lower(@keyword))>0))
                    OR matches.hits>0 OR assignees.hits>0 OR (@completion IS NOT NULL AND c.due_complete=@completion) OR deadline.hit
                    OR (@recent IS NOT NULL AND c.updated_at>=@recent)))
              )
            ORDER BY c.id LIMIT 51;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", board.OrganizationId); command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("labels", NpgsqlDbType.Array | NpgsqlDbType.Uuid, filter.LabelIds.ToArray());
        command.Parameters.AddWithValue("members", NpgsqlDbType.Array | NpgsqlDbType.Uuid, (filter.MemberIds ?? []).ToArray());
        command.Parameters.AddWithValue("verified", filter.RequireVerifiedEmail);
        command.Parameters.AddWithValue("completion", NpgsqlDbType.Boolean, (object?)filter.DueComplete ?? DBNull.Value);
        command.Parameters.AddWithValue("due", filter.DueState);
        command.Parameters.AddWithValue("recent", NpgsqlDbType.TimestampTz, (object?)filter.UpdatedSince?.ToUniversalTime() ?? DBNull.Value);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, (filter.EvaluatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime());
        command.Parameters.AddWithValue("keyword", filter.Keyword); command.Parameters.AddWithValue("all", filter.MatchAll);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardRecord>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCard(reader));
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public async Task<IReadOnlyList<CardRecord>> FilterBoardCardsAsync(Guid boardId, BoardCardFilter filter, Guid? after, CancellationToken cancellationToken = default)
    {
        var memberIds = filter.MemberIds ?? []; var eligible = new HashSet<Guid>();
        if (memberIds.Count > 0)
        {
            var board = await FindBoardAsync(boardId, cancellationToken); if (board is null) return [];
            var profiles = await organizations.ListActiveMembersAsync(board.OrganizationId, null, cancellationToken, userIds: memberIds);
            eligible = profiles.Where(p => p.AccountStatus == AccountStatus.Active && (!filter.RequireVerifiedEmail || p.EmailVerified)).Select(p => p.UserId).ToHashSet();
        }
        lock (_sync)
        {
            bool Matches(CardRecord card)
            {
                if (filter.Keyword.Length == 0 && filter.LabelIds.Count == 0 && memberIds.Count == 0 && filter.DueComplete is null && filter.DueState == "all" && filter.UpdatedSince is null) return true;
                var text = card.Title.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase)
                    || (card.Description ?? "").Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase);
                bool Assigned(Guid id) => _labels.TryGetValue(id, out var label) && !label.Deleted
                    && label.OrganizationId == card.OrganizationId && label.BoardId == boardId && _cardLabels.Contains((card.Id, id));
                bool MemberAssigned(Guid id) => eligible.Contains(id) && _members.TryGetValue((boardId, id), out var m) && m.Active && _cardMembers.ContainsKey((card.Id, id));
                var now = filter.EvaluatedAt ?? DateTimeOffset.UtcNow;
                var deadline = filter.DueState switch {
                    "none" => card.DueAt is null,
                    "overdue" => card.DueAt is { } past && !card.DueComplete && past < now,
                    "upcoming" => card.DueAt is { } future && !card.DueComplete && future >= now,
                    _ => false };
                return filter.MatchAll ? (filter.Keyword.Length == 0 || text) && filter.LabelIds.All(Assigned) && memberIds.All(MemberAssigned)
                        && (filter.DueComplete is null || card.DueComplete == filter.DueComplete) && (filter.DueState == "all" || deadline)
                        && (filter.UpdatedSince is null || card.UpdatedAt >= filter.UpdatedSince)
                    : (filter.Keyword.Length > 0 && text) || filter.LabelIds.Any(Assigned) || memberIds.Any(MemberAssigned)
                        || (filter.DueComplete is not null && card.DueComplete == filter.DueComplete) || deadline
                        || (filter.UpdatedSince is not null && card.UpdatedAt >= filter.UpdatedSince);
            }
            return _cards.Values.Where(card => card.BoardId == boardId
                && card.LifecycleState == WorkItemLifecycleState.Active && _lists.TryGetValue(card.ListId, out var parent)
                && parent.BoardId == card.BoardId && parent.OrganizationId == card.OrganizationId && parent.LifecycleState == WorkItemLifecycleState.Active
                && (after is null || card.Id.CompareTo(after.Value) > 0) && Matches(card)).OrderBy(card => card.Id).Take(51).ToArray();
        }
    }
}
