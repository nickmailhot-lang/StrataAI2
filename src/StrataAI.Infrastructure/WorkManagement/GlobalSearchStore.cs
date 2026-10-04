using Npgsql;
using NpgsqlTypes;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<IReadOnlyList<CardRecord>> SearchBoardCardsAsync(Guid boardId, GlobalSearchBinding binding,
        bool requireVerifiedEmail, Guid? after, CancellationToken cancellationToken = default)
    {
        var board = await FindBoardAsync(boardId, cancellationToken);
        if (board is null) return [];
        if (!connectionFactory.HasCommandScope(board.OrganizationId))
            throw new InvalidOperationException("Search content requires the owning authorized Board transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(board.OrganizationId, cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT c.id,c.tenant_id,c.board_id,c.list_id,c.title,c.description,c.rank,c.lifecycle_state,
              c.created_at,c.updated_at,c.version,c.start_at,c.due_at,c.due_timezone,c.due_has_time,c.due_complete,c.archived_at,c.deleted_at,c.deleted_by
            FROM cards c JOIN board_lists l ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
            JOIN boards b ON b.tenant_id=c.tenant_id AND b.id=c.board_id
            CROSS JOIN LATERAL (SELECT
              (strpos(lower(c.title),lower(@q))>0 OR strpos(lower(coalesce(c.description,'')),lower(@q))>0) AS text_hit,
              EXISTS(SELECT 1 FROM card_labels a JOIN board_labels d
                ON d.tenant_id=a.tenant_id AND d.board_id=a.board_id AND d.id=a.label_id
                WHERE a.tenant_id=c.tenant_id AND a.board_id=c.board_id AND a.card_id=c.id
                  AND d.status='ACTIVE' AND strpos(lower(d.name),lower(@label))>0) AS label_hit,
              EXISTS(SELECT 1 FROM card_members a
                JOIN board_members m ON m.tenant_id=a.tenant_id AND m.board_id=a.board_id AND m.user_id=a.user_id
                JOIN organization_members o ON o.tenant_id=a.tenant_id AND o.user_id=a.user_id
                JOIN users u ON u.id=a.user_id
                WHERE a.tenant_id=c.tenant_id AND a.board_id=c.board_id AND a.card_id=c.id
                  AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE'
                  AND (NOT @verified OR u.email_verified) AND strpos(lower(u.display_name),lower(@member))>0) AS member_hit
            ) matches
            WHERE c.tenant_id=@tenant AND c.board_id=@board AND (@after IS NULL OR c.id>@after)
              AND c.lifecycle_state<>'DELETED' AND l.lifecycle_state<>'DELETED' AND b.lifecycle_state<>'DELETED'
              AND CASE WHEN @archive THEN c.lifecycle_state='ARCHIVED' OR l.lifecycle_state='ARCHIVED' OR b.lifecycle_state='ARCHIVED'
                ELSE c.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE' AND b.lifecycle_state='ACTIVE' END
              AND ((@q='' AND @label='' AND @member='')
                OR (@all AND (@q='' OR text_hit) AND (@label='' OR label_hit) AND (@member='' OR member_hit))
                OR (NOT @all AND ((@q<>'' AND text_hit) OR (@label<>'' AND label_hit) OR (@member<>'' AND member_hit))))
            ORDER BY c.id LIMIT 51;
            """, session.Connection, session.Transaction);
        query.Parameters.AddWithValue("tenant", board.OrganizationId); query.Parameters.AddWithValue("board", boardId);
        query.Parameters.AddWithValue("q", binding.Keyword); query.Parameters.AddWithValue("label", binding.Label);
        query.Parameters.AddWithValue("member", binding.Member); query.Parameters.AddWithValue("all", binding.MatchAll);
        query.Parameters.AddWithValue("archive", binding.Scope == GlobalSearchLifecycleScope.Archived);
        query.Parameters.AddWithValue("verified", requireVerifiedEmail);
        query.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        var result = new List<CardRecord>(); await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCard(reader));
        return result;
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public async Task<IReadOnlyList<CardRecord>> SearchBoardCardsAsync(Guid boardId, GlobalSearchBinding binding,
        bool requireVerifiedEmail, Guid? after, CancellationToken cancellationToken = default)
    {
        CardRecord[] candidates;
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board) || board.LifecycleState == BoardLifecycleState.Deleted) return [];
            candidates = _cards.Values.Where(c => c.BoardId == boardId && c.OrganizationId == board.OrganizationId
                && (after is null || c.Id.CompareTo(after.Value) > 0) && c.LifecycleState != WorkItemLifecycleState.Deleted
                && _lists.TryGetValue(c.ListId, out var l) && l.BoardId == boardId && l.OrganizationId == c.OrganizationId
                && l.LifecycleState != WorkItemLifecycleState.Deleted
                && (binding.Scope == GlobalSearchLifecycleScope.Archived
                    ? c.LifecycleState == WorkItemLifecycleState.Archived || l.LifecycleState == WorkItemLifecycleState.Archived || board.LifecycleState == BoardLifecycleState.Archived
                    : c.LifecycleState == WorkItemLifecycleState.Active && l.LifecycleState == WorkItemLifecycleState.Active && board.LifecycleState == BoardLifecycleState.Active))
                .OrderBy(c => c.Id).ToArray();
        }
        var result = new List<CardRecord>();
        foreach (var card in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = card.Title.Contains(binding.Keyword, StringComparison.OrdinalIgnoreCase)
                || (card.Description ?? "").Contains(binding.Keyword, StringComparison.OrdinalIgnoreCase);
            bool label;
            lock (_sync) label = _labels.Values.Any(l => l.OrganizationId == card.OrganizationId && l.BoardId == boardId
                && !l.Deleted && _cardLabels.Contains((card.Id, l.Id)) && l.Name.Contains(binding.Label, StringComparison.OrdinalIgnoreCase));
            var member = false; Guid? memberAfter = null;
            if (binding.Member.Length > 0)
                do
                {
                    var page = await ListCardMembersAsync(card.Id, memberAfter, requireVerifiedEmail, cancellationToken);
                    member = page.Any(m => m.DisplayName.Contains(binding.Member, StringComparison.OrdinalIgnoreCase));
                    if (member || page.Count < 51) break;
                    memberAfter = page[^1].UserId;
                } while (true);
            var matches = binding.Keyword.Length == 0 && binding.Label.Length == 0 && binding.Member.Length == 0
                || (binding.MatchAll ? (binding.Keyword.Length == 0 || text) && (binding.Label.Length == 0 || label) && (binding.Member.Length == 0 || member)
                    : binding.Keyword.Length > 0 && text || binding.Label.Length > 0 && label || binding.Member.Length > 0 && member);
            if (matches) result.Add(card);
            if (result.Count == 51) break;
        }
        return result;
    }
}
