using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task CopyBoardContentsAsync(Guid sourceBoardId, Guid destinationBoardId, DateTimeOffset now, CancellationToken ct)
    {
        var source = await FindBoardAsync(sourceBoardId, ct); var target = await FindBoardAsync(destinationBoardId, ct);
        if (source is null || target is null || source.Id == target.Id || source.OrganizationId != target.OrganizationId
            || target.Version != 1 || target.CreatedAt.UtcTicks / 10 != now.UtcTicks / 10 || !connectionFactory.HasCommandScope(source.OrganizationId))
            throw new InvalidOperationException("Board copy requires its newly created target and owning transaction.");
        await using var session = await connectionFactory.OpenTenantSessionAsync(source.OrganizationId, ct);
        await using var command = new NpgsqlCommand("""
            WITH mapped_lists AS MATERIALIZED (
              SELECT *,gen_random_uuid() AS copied_id FROM board_lists
              WHERE tenant_id=@tenant AND board_id=@source AND lifecycle_state<>'DELETED'
            ), inserted_lists AS (
              INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,created_at,updated_at,version,archived_at)
              SELECT copied_id,@tenant,@target,name,rank,lifecycle_state,@now,@now,1,
                CASE WHEN lifecycle_state='ARCHIVED' THEN @now ELSE NULL END FROM mapped_lists RETURNING id
            ), mapped_cards AS MATERIALIZED (
              SELECT c.*,l.copied_id AS copied_list_id,gen_random_uuid() AS copied_id
              FROM cards c JOIN mapped_lists l ON l.id=c.list_id AND l.tenant_id=c.tenant_id AND l.board_id=c.board_id
              JOIN inserted_lists parent ON parent.id=l.copied_id
              WHERE c.tenant_id=@tenant AND c.board_id=@source AND c.lifecycle_state<>'DELETED'
            ), inserted_cards AS (
              INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,
                version,archived_at,start_at,due_at,due_timezone,due_has_time,due_complete)
              SELECT copied_id,@tenant,@target,copied_list_id,title,description,rank,lifecycle_state,@now,@now,1,
                CASE WHEN lifecycle_state='ARCHIVED' THEN @now ELSE NULL END,start_at,due_at,due_timezone,due_has_time,false
              FROM mapped_cards RETURNING id
            ), mapped_labels AS MATERIALIZED (
              SELECT *,gen_random_uuid() AS copied_id FROM board_labels
              WHERE tenant_id=@tenant AND board_id=@source AND status='ACTIVE'
            ), inserted_labels AS (
              INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,status,created_at,updated_at,version)
              SELECT copied_id,@tenant,@target,name,color,rank,'ACTIVE',@now,@now,1 FROM mapped_labels RETURNING id
            ), mapped_checklists AS MATERIALIZED (
              SELECT c.*,m.copied_id AS copied_card_id,gen_random_uuid() AS copied_id
              FROM checklists c JOIN mapped_cards m ON m.id=c.card_id AND m.tenant_id=c.tenant_id
              JOIN inserted_cards parent ON parent.id=m.copied_id WHERE c.tenant_id=@tenant AND c.deleted_at IS NULL
            ), inserted_checklists AS (
              INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at,version)
              SELECT copied_id,@tenant,copied_card_id,title,rank,@now,@now,1 FROM mapped_checklists RETURNING id
            ), inserted_items AS (
              INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,completed,completed_at,completed_by,created_at,updated_at,version)
              SELECT gen_random_uuid(),@tenant,m.copied_id,i.text,i.rank,false,NULL,NULL,@now,@now,1
              FROM mapped_checklists m JOIN inserted_checklists parent ON parent.id=m.copied_id
              JOIN checklist_items i ON i.tenant_id=m.tenant_id AND i.checklist_id=m.id WHERE i.deleted_at IS NULL RETURNING id
            )
            INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at,version)
              SELECT @tenant,@target,c.copied_id,l.copied_id,@now,@now,1
              FROM card_labels a JOIN mapped_cards c ON c.id=a.card_id AND c.tenant_id=a.tenant_id AND c.board_id=a.board_id
              JOIN inserted_cards card ON card.id=c.copied_id
              JOIN mapped_labels l ON l.id=a.label_id AND l.tenant_id=a.tenant_id AND l.board_id=a.board_id
              JOIN inserted_labels label ON label.id=l.copied_id
              WHERE a.tenant_id=@tenant AND a.board_id=@source;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", source.OrganizationId); command.Parameters.AddWithValue("source", sourceBoardId);
        command.Parameters.AddWithValue("target", destinationBoardId); command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(ct); await session.CommitAsync(ct);
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public Task CopyBoardContentsAsync(Guid sourceBoardId, Guid destinationBoardId, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var source = _boards[sourceBoardId]; var target = _boards[destinationBoardId];
            if (source.Id == target.Id || source.OrganizationId != target.OrganizationId || target.Version != 1
                || target.CreatedAt != now || !transactionScope.Owns(source.OrganizationId))
                throw new InvalidOperationException("Board copy requires its newly created target and owning transaction.");
            var lists = _lists.Values.Where(l => l.OrganizationId == source.OrganizationId && l.BoardId == sourceBoardId
                && l.LifecycleState != WorkItemLifecycleState.Deleted).ToArray();
            var listMap = lists.ToDictionary(l => l.Id, _ => Guid.NewGuid());
            foreach (var list in lists)
            {
                var copy = list with { Id = listMap[list.Id], BoardId = target.Id, CreatedAt = now, UpdatedAt = now, Version = 1,
                    ArchivedAt = list.LifecycleState == WorkItemLifecycleState.Archived ? now : null, DeletedAt = null, DeletedBy = null };
                _lists.Add(copy.Id, copy);
            }
            var cards = _cards.Values.Where(c => c.OrganizationId == source.OrganizationId && c.BoardId == sourceBoardId
                && listMap.ContainsKey(c.ListId) && c.LifecycleState != WorkItemLifecycleState.Deleted).ToArray();
            var cardMap = cards.ToDictionary(c => c.Id, _ => Guid.NewGuid());
            foreach (var card in cards)
            {
                var copy = card with { Id = cardMap[card.Id], BoardId = target.Id, ListId = listMap[card.ListId],
                    CreatedAt = now, UpdatedAt = now, Version = 1, DueComplete = false, HasCover = null,
                    ArchivedAt = card.LifecycleState == WorkItemLifecycleState.Archived ? now : null, DeletedAt = null, DeletedBy = null };
                _cards.Add(copy.Id, copy);
            }
            var labels = _labels.Values.Where(l => l.OrganizationId == source.OrganizationId && l.BoardId == sourceBoardId && !l.Deleted).ToArray();
            var labelMap = labels.ToDictionary(l => l.Id, _ => Guid.NewGuid());
            foreach (var label in labels)
            {
                var copy = label with { Id = labelMap[label.Id], BoardId = target.Id, CreatedAt = now, UpdatedAt = now, Version = 1 };
                _labels.Add(copy.Id, copy);
            }
            foreach (var association in _cardLabels.Where(a => cardMap.ContainsKey(a.CardId) && labelMap.ContainsKey(a.LabelId)).ToArray())
                _cardLabels.Add((cardMap[association.CardId], labelMap[association.LabelId]));
            foreach (var checklist in _checklists.Values.Where(c => c.OrganizationId == source.OrganizationId && cardMap.ContainsKey(c.CardId) && c.DeletedAt is null).ToArray())
            {
                var copy = checklist with { Id = Guid.NewGuid(), CardId = cardMap[checklist.CardId], CreatedAt = now, UpdatedAt = now, Version = 1 };
                _checklists.Add(copy.Id, copy);
                foreach (var item in _checklistItems.Values.Where(i => i.OrganizationId == source.OrganizationId && i.ChecklistId == checklist.Id && i.DeletedAt is null).ToArray())
                {
                    var child = item with { Id = Guid.NewGuid(), ChecklistId = copy.Id, Completed = false, CompletedAt = null, CompletedBy = null,
                        CreatedAt = now, UpdatedAt = now, Version = 1 };
                    _checklistItems.Add(child.Id, child);
                }
            }
            return Task.CompletedTask;
        }
    }
}
