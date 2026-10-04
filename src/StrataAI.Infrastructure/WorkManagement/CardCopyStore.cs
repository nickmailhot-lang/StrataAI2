using Npgsql;
using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class PostgresWorkManagementStore
{
    public async Task<CardRecord?> CopyCardAsync(Guid sourceCardId, Guid destinationListId, Guid copiedCardId,
        string title, long expectedVersion, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var source = await FindCardAsync(sourceCardId, cancellationToken);
        var destination = await FindListAsync(destinationListId, cancellationToken);
        if (source is null || destination is null || source.OrganizationId != destination.OrganizationId
            || source.Version != expectedVersion || source.LifecycleState != WorkItemLifecycleState.Active
            || destination.LifecycleState != WorkItemLifecycleState.Active) return null;
        if (!connectionFactory.HasCommandScope(source.OrganizationId)) throw new InvalidOperationException("Card copy requires its owning transaction.");
        var copied = await CreateCardAsync(destinationListId, copiedCardId, title, source.Description, null, createdAt, cancellationToken);
        await using var session = await connectionFactory.OpenTenantSessionAsync(source.OrganizationId, cancellationToken);
        var labels = new List<BoardLabelRecord>();
        await using (var query = new NpgsqlCommand($"SELECT {string.Join(',', LabelColumns.Split(',').Select(c => "label." + c))} " +
            "FROM board_labels label JOIN card_labels a ON a.tenant_id=label.tenant_id AND a.board_id=label.board_id AND a.label_id=label.id " +
            "WHERE a.tenant_id=@tenant AND a.board_id=@source AND a.card_id=@card AND label.status='ACTIVE' ORDER BY label.rank,label.id;",
            session.Connection, session.Transaction))
        {
            query.Parameters.AddWithValue("tenant", source.OrganizationId); query.Parameters.AddWithValue("source", source.BoardId);
            query.Parameters.AddWithValue("card", source.Id);
            await using var rows = await query.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken)) labels.Add(ReadLabel(rows));
        }
        var targets = new List<Guid>();
        foreach (var label in labels) targets.Add(source.BoardId == destination.BoardId ? label.Id
            : (await CreateLabelAsync(destination.BoardId, Guid.NewGuid(), label.Name, label.Color, createdAt, cancellationToken)).Id);
        await using var command = new NpgsqlCommand("""
            UPDATE cards SET start_at=source.start_at,due_at=source.due_at,due_timezone=source.due_timezone,
              due_has_time=source.due_has_time,due_complete=false
            FROM cards source WHERE cards.tenant_id=@tenant AND cards.id=@copy AND source.tenant_id=@tenant AND source.id=@source;
            INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,created_at,updated_at,version)
              SELECT @tenant,@board,@copy,label,@now,@now,1 FROM unnest(@labels::uuid[]) label;
            WITH mapped AS MATERIALIZED (
              SELECT *,gen_random_uuid() AS copied_id FROM checklists WHERE tenant_id=@tenant AND card_id=@source AND deleted_at IS NULL
            ), inserted AS (
              INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at,version)
              SELECT copied_id,tenant_id,@copy,title,rank,@now,@now,1 FROM mapped RETURNING id
            )
            INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,completed,completed_at,completed_by,created_at,updated_at,version)
              SELECT gen_random_uuid(),m.tenant_id,m.copied_id,i.text,i.rank,false,NULL,NULL,@now,@now,1
              FROM mapped m JOIN inserted p ON p.id=m.copied_id
              JOIN checklist_items i ON i.tenant_id=m.tenant_id AND i.checklist_id=m.id WHERE i.deleted_at IS NULL;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("tenant", source.OrganizationId); command.Parameters.AddWithValue("copy", copied.Id);
        command.Parameters.AddWithValue("source", source.Id); command.Parameters.AddWithValue("board", destination.BoardId);
        command.Parameters.AddWithValue("labels", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid, targets.ToArray());
        command.Parameters.AddWithValue("now", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
        return copied with { StartAt = source.StartAt, DueAt = source.DueAt, DueTimezone = source.DueTimezone, DueHasTime = source.DueHasTime, DueComplete = false };
    }
}

internal sealed partial class InMemoryWorkManagementStore
{
    public Task<CardRecord?> CopyCardAsync(Guid sourceCardId, Guid destinationListId, Guid copiedCardId,
        string title, long expectedVersion, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_cards.TryGetValue(sourceCardId, out var source) || !_lists.TryGetValue(destinationListId, out var destination)
                || source.OrganizationId != destination.OrganizationId || source.Version != expectedVersion
                || source.LifecycleState != WorkItemLifecycleState.Active || destination.LifecycleState != WorkItemLifecycleState.Active)
                return Task.FromResult<CardRecord?>(null);
            if (!transactionScope.Owns(source.OrganizationId)) throw new InvalidOperationException("Card copy requires its owning transaction.");
            var rank = RankToken.After(_cards.Values.Where(c => c.ListId == destinationListId && c.LifecycleState == WorkItemLifecycleState.Active)
                .Select(c => c.Rank).Order(StringComparer.Ordinal).LastOrDefault());
            var copied = source with { Id = copiedCardId, BoardId = destination.BoardId, ListId = destination.Id, Title = title,
                Rank = rank, CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1, DueComplete = false, HasCover = null };
            // Build all rank-sensitive definitions before publishing any state.
            var labels = _cardLabels.Where(a => a.CardId == source.Id && _labels.TryGetValue(a.LabelId, out var l) && !l.Deleted)
                .Select(a => _labels[a.LabelId]).OrderBy(l => l.Rank, StringComparer.Ordinal).ThenBy(l => l.Id).ToArray();
            var last = _labels.Values.Where(l => l.BoardId == destination.BoardId && !l.Deleted)
                .Select(l => l.Rank).Order(StringComparer.Ordinal).LastOrDefault();
            var targets = new List<BoardLabelRecord>();
            foreach (var label in labels)
            {
                if (source.BoardId == destination.BoardId) targets.Add(label);
                else
                {
                    last = RankToken.After(last);
                    targets.Add(label with { Id = Guid.NewGuid(), BoardId = destination.BoardId, Rank = last,
                        CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1 });
                }
            }
            _cards.Add(copied.Id, copied);
            foreach (var label in targets) { _labels.TryAdd(label.Id, label); _cardLabels.Add((copied.Id, label.Id)); }
            foreach (var checklist in _checklists.Values.Where(c => c.OrganizationId == source.OrganizationId && c.CardId == source.Id && c.DeletedAt is null).ToArray())
            {
                var child = checklist with { Id = Guid.NewGuid(), CardId = copied.Id, CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1 };
                _checklists.Add(child.Id, child);
                foreach (var item in _checklistItems.Values.Where(i => i.OrganizationId == source.OrganizationId && i.ChecklistId == checklist.Id && i.DeletedAt is null).ToArray())
                {
                    var copy = item with { Id = Guid.NewGuid(), ChecklistId = child.Id, Completed = false, CompletedAt = null, CompletedBy = null,
                        CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1 };
                    _checklistItems.Add(copy.Id, copy);
                }
            }
            return Task.FromResult<CardRecord?>(copied);
        }
    }
}
