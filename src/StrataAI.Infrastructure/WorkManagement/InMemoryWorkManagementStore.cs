using StrataAI.Application.WorkManagement;
using StrataAI.Application.Organizations;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore(IOrganizationStore organizations, DemoWorkTransactionScope transactionScope) : IWorkManagementStore, ICardDateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, BoardRecord> _boards = [];
    private readonly Dictionary<Guid, BoardListRecord> _lists = [];
    private readonly Dictionary<Guid, CardRecord> _cards = [];
    private readonly Dictionary<(Guid BoardId, Guid UserId), BoardMemberRecord> _members = [];
    private readonly Dictionary<(Guid BoardId, Guid UserId), StoredBoardStarPreference> _starred = [];
    private readonly Dictionary<(Guid BoardId, Guid UserId, long Version), BoardStarEvent> _starEvents = [];
    public Task<bool> AcquireOrganizationReadScopeAsync(Guid organizationId, Guid actorId,
        CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> AcquireBoardReadScopeAsync(Guid organizationId, Guid actorId,
        Guid boardId, CancellationToken cancellationToken = default) =>
        AcquireCommandScopeAsync(organizationId, actorId, boardId, cancellationToken);

    public Task<bool> AcquireCommandScopeAsync(Guid organizationId, Guid actorId,
        Guid? boardId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(boardId is null ||
                _boards.TryGetValue(boardId.Value, out var board) && board.OrganizationId == organizationId);
        }
    }

    public Task<IReadOnlyList<OrganizationBoardSummary>> ListVisibleBoardsAsync(
        Guid organizationId,
        Guid userId,
        bool organizationAdministrator,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<OrganizationBoardSummary>>(
                _boards.Values.Where(board => board.OrganizationId == organizationId &&
                    board.LifecycleState == BoardLifecycleState.Active &&
                    (board.Visibility != BoardVisibility.Private || organizationAdministrator ||
                        (_members.TryGetValue((board.Id, userId), out var member) && member.Active)))
                    .OrderBy(board => board.Name).ThenBy(board => board.Id)
                    .Select(board => new OrganizationBoardSummary(board.Id, board.Name, board.Version))
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<OrganizationBoardSummary>> ListVisibleBoardsPageAsync(
        Guid organizationId, Guid userId, bool organizationAdministrator, Guid? after, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<OrganizationBoardSummary>>(
            _boards.Values.Where(b => b.OrganizationId == organizationId && b.LifecycleState != BoardLifecycleState.Deleted
                && (after is null || b.Id.CompareTo(after.Value) > 0)
                && (b.Visibility != BoardVisibility.Private || organizationAdministrator
                    || (_members.TryGetValue((b.Id, userId), out var member) && member.Active)))
            .OrderBy(b => b.Id).Take(51).Select(b => new OrganizationBoardSummary(b.Id, b.Name, b.Version)).ToArray());
    }

    public Task<IReadOnlyList<ArchivedListEntry>> ListArchivedListsAsync(Guid boardId, Guid? after,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ArchivedListEntry>>(_lists.Values
                .Where(list => list.BoardId == boardId && list.LifecycleState == WorkItemLifecycleState.Archived
                    && (after is null || list.Id.CompareTo(after.Value) > 0))
                .OrderBy(list => list.Id).Take(51)
                .Select(list => new ArchivedListEntry(list, _cards.Values.LongCount(card =>
                    card.OrganizationId == list.OrganizationId && card.BoardId == boardId && card.ListId == list.Id
                    && card.LifecycleState != WorkItemLifecycleState.Deleted))).ToArray());
        }
    }

    public Task<IReadOnlyList<ArchivedCardEntry>> ListArchivedCardsAsync(Guid boardId, Guid? after,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ArchivedCardEntry>>(_cards.Values
                .Where(card => card.BoardId == boardId && card.LifecycleState == WorkItemLifecycleState.Archived
                    && (after is null || card.Id.CompareTo(after.Value) > 0)
                    && _lists.TryGetValue(card.ListId, out var list) && list.BoardId == boardId
                    && list.OrganizationId == card.OrganizationId && list.LifecycleState != WorkItemLifecycleState.Deleted)
                .OrderBy(card => card.Id).Take(51)
                .Select(card => new ArchivedCardEntry(card with { Description = null }, _lists[card.ListId])).ToArray());
        }
    }

    public Task<BoardRecord> CreateBoardAsync(
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
        lock (_sync)
        {
            var board = new BoardRecord(
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

            _boards[boardId] = board;
            _members[(boardId, actorUserId)] =
                new BoardMemberRecord(
                    boardId,
                    actorUserId,
                    BoardRole.Admin,
                    true,
                    createdAt,
                    createdAt,
                    1);

            return Task.FromResult(board);
        }
    }

    public Task<BoardRecord?> FindBoardAsync(
        Guid boardId,
        CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        lock (_sync)
        {
            _boards.TryGetValue(boardId, out var board);
            return Task.FromResult<BoardRecord?>(board is { LifecycleState: BoardLifecycleState.Deleted } && !includeDeleted ? null : board);
        }
    }

    public Task<BoardMemberRecord?> FindBoardMemberAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        lock (_sync)
        {
            _members.TryGetValue((boardId, userId), out var member);
            return Task.FromResult<BoardMemberRecord?>(_boards.TryGetValue(boardId, out var board) &&
                (includeDeleted || board.LifecycleState != BoardLifecycleState.Deleted) ? member : null);
        }
    }

    public Task<BoardSnapshot?> GetSnapshotAsync(
        Guid boardId,
        Guid? userId,
        BoardAccess access,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board))
            {
                return Task.FromResult<BoardSnapshot?>(null);
            }

            var lists = _lists.Values
                .Where(list =>
                    list.BoardId == boardId &&
                    list.LifecycleState == WorkItemLifecycleState.Active)
                .OrderBy(list => list.Rank, StringComparer.Ordinal)
                .Select(
                    list =>
                        new BoardListSnapshot(
                            list,
                            _cards.Values
                                .Where(card =>
                                    card.ListId == list.Id &&
                                    card.LifecycleState == WorkItemLifecycleState.Active)
                                .OrderBy(card => card.Rank, StringComparer.Ordinal)
                                .Select(card => card with { HasCover = _cardCovers.ContainsKey(card.Id) })
                                .ToArray()))
                .ToArray();

            var starred =
                userId.HasValue && _starred.TryGetValue((boardId, userId.Value), out var preference) && preference.Starred;

            return Task.FromResult<BoardSnapshot?>(
                  new BoardSnapshot(board, lists, starred, access, LabelPreviews(lists)));
        }
    }

    public Task<BoardRecord?> UpdateBoardAsync(
        Guid boardId,
        string name,
        string? description,
        string backgroundType,
        string? backgroundValue,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board) ||
                board.Version != expectedVersion)
            {
                return Task.FromResult<BoardRecord?>(null);
            }

            var updated = board with
            {
                Name = name,
                Description = description,
                BackgroundType = backgroundType,
                BackgroundValue = backgroundValue,
                UpdatedAt = updatedAt,
                Version = board.Version + 1,
            };

            _boards[boardId] = updated;
            return Task.FromResult<BoardRecord?>(updated);
        }
    }

    public Task<BoardRecord?> SetBoardVisibilityAsync(
        Guid boardId,
        BoardVisibility visibility,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board) ||
                board.Version != expectedVersion)
            {
                return Task.FromResult<BoardRecord?>(null);
            }

            var updated = board with
            {
                Visibility = visibility,
                UpdatedAt = updatedAt,
                Version = board.Version + 1,
            };

            _boards[boardId] = updated;
            return Task.FromResult<BoardRecord?>(updated);
        }
    }

    public Task<BoardRecord?> SetBoardLifecycleAsync(
        Guid boardId,
        BoardLifecycleState expectedState,
        BoardLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        if (nextState == BoardLifecycleState.Deleted && (actorUserId is null || actorUserId == Guid.Empty))
            throw new ArgumentException("Deletion requires an actor.", nameof(actorUserId));
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board) ||
                board.Version != expectedVersion ||
                board.LifecycleState != expectedState)
            {
                return Task.FromResult<BoardRecord?>(null);
            }

            var updated = board with
            {
                LifecycleState = nextState,
                ArchivedAt = nextState == BoardLifecycleState.Archived ? updatedAt : nextState == BoardLifecycleState.Active ? null : board.ArchivedAt,
                DeletedAt = nextState == BoardLifecycleState.Deleted ? updatedAt : board.DeletedAt,
                DeletedBy = nextState == BoardLifecycleState.Deleted ? actorUserId : board.DeletedBy,
                UpdatedAt = updatedAt,
                Version = board.Version + 1,
            };

            _boards[boardId] = updated;
            return Task.FromResult<BoardRecord?>(updated);
        }
    }

    public Task SetStarAsync(
        Guid boardId,
        Guid userId,
        bool starred,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var key = (boardId, userId);
            if (!_starred.TryGetValue(key, out var current))
                _starred[key] = new(starred, updatedAt, updatedAt, 1, Guid.NewGuid());
            else if (current.Starred != starred)
                _starred[key] = current with { Starred = starred, UpdatedAt = updatedAt > current.UpdatedAt ? updatedAt : current.UpdatedAt,
                    Version = checked(current.Version + 1) };
            else return Task.CompletedTask;
            var preference = _starred[key];
            _starEvents[(boardId,userId,preference.Version)] = new(Guid.NewGuid(),userId,_boards[boardId].OrganizationId,
                boardId,preference.Id,preference.Version,preference.UpdatedAt);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BoardMemberRecord>> ListBoardMembersAsync(
        Guid boardId,
        CancellationToken cancellationToken = default, Guid? after = null, int? limit = null)
    {
        lock (_sync)
        {
            var result = _members.Values
                .Where(member => member.BoardId == boardId && member.Active && (after is null || member.UserId.CompareTo(after.Value) > 0))
                .OrderBy(member => member.UserId)
                .Take(limit ?? int.MaxValue)
                .ToArray();

            return Task.FromResult<IReadOnlyList<BoardMemberRecord>>(result);
        }
    }

    public Task<BoardMemberRecord> UpsertBoardMemberAsync(
        Guid boardId,
        Guid userId,
        BoardRole role,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_members.TryGetValue((boardId, userId), out var existing))
            {
                var updated = existing with
                {
                    Role = role,
                    Active = true,
                    UpdatedAt = updatedAt,
                    Version = existing.Version + 1,
                };
                _members[(boardId, userId)] = updated;
                return Task.FromResult(updated);
            }

            var created = new BoardMemberRecord(
                boardId,
                userId,
                role,
                true,
                updatedAt,
                updatedAt,
                1);
            _members[(boardId, userId)] = created;
            return Task.FromResult(created);
        }
    }

    public Task<bool> RemoveBoardMemberAsync(
        Guid boardId,
        Guid userId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_members.TryGetValue((boardId, userId), out var existing) ||
                !existing.Active)
            {
                return Task.FromResult(false);
            }

            _members[(boardId, userId)] = existing with
            {
                Active = false,
                UpdatedAt = updatedAt,
                Version = existing.Version + 1,
            };

            return Task.FromResult(true);
        }
    }

    public Task<BoardListRecord> CreateListAsync(
        Guid boardId,
        Guid listId,
        string name,
        string? rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board))
            {
                throw new InvalidOperationException("Board was not found.");
            }

            rank ??= RankToken.After(_lists.Values
                .Where(item => item.BoardId == boardId && item.LifecycleState == WorkItemLifecycleState.Active)
                .Select(item => item.Rank).Order(StringComparer.Ordinal).LastOrDefault());

            var list = new BoardListRecord(
                listId,
                board.OrganizationId,
                boardId,
                name,
                rank,
                WorkItemLifecycleState.Active,
                createdAt,
                createdAt,
                1);

            _lists[listId] = list;
            return Task.FromResult(list);
        }
    }

    public Task<BoardListRecord> CopyListAsync(Guid sourceListId, Guid destinationBoardId,
        Guid copiedListId, string name, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var source = _lists[sourceListId]; var board = _boards[destinationBoardId];
            if (source.OrganizationId != board.OrganizationId) throw new InvalidOperationException("Invalid copy scope.");
            var rank = RankToken.After(_lists.Values.Where(item => item.BoardId == destinationBoardId
                && item.LifecycleState == WorkItemLifecycleState.Active).Select(item => item.Rank).Order(StringComparer.Ordinal).LastOrDefault());
              var sourceCards = _cards.Values.Where(item => item.OrganizationId == source.OrganizationId
                && item.BoardId == source.BoardId && item.ListId == sourceListId && item.LifecycleState != WorkItemLifecycleState.Deleted)
                  .ToArray();
              var cards = sourceCards.Select(item => item with { Id = Guid.NewGuid(), BoardId = destinationBoardId, ListId = copiedListId,
                    CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1,
                    ArchivedAt = item.LifecycleState == WorkItemLifecycleState.Archived ? createdAt : null,
                    DeletedAt = null, DeletedBy = null }).ToArray();
              var sourceIds = sourceCards.Select(item => item.Id).ToHashSet();
              var associations = _cardLabels.Where(item => sourceIds.Contains(item.CardId) && _labels.TryGetValue(item.LabelId, out var label) && !label.Deleted).ToArray();
              var labelMap = new Dictionary<Guid, BoardLabelRecord>();
              var lastLabelRank = _labels.Values.Where(item => item.BoardId == destinationBoardId && !item.Deleted).Select(item => item.Rank).Order(StringComparer.Ordinal).LastOrDefault();
              foreach (var label in associations.Select(item => _labels[item.LabelId]).DistinctBy(item => item.Id).OrderBy(item => item.Rank, StringComparer.Ordinal).ThenBy(item => item.Id))
              {
                  var target = label;
                  if (source.BoardId != destinationBoardId)
                  {
                      lastLabelRank = RankToken.After(lastLabelRank);
                      target = label with { Id = Guid.NewGuid(), BoardId = destinationBoardId, Rank = lastLabelRank, CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1 };
                  }
                  labelMap.Add(label.Id, target);
              }
            var copied = new BoardListRecord(copiedListId, board.OrganizationId, board.Id, name, rank,
                WorkItemLifecycleState.Active, createdAt, createdAt, 1);
            _lists.Add(copied.Id, copied);
            foreach (var card in cards) _cards.Add(card.Id, card);
              foreach (var label in labelMap.Values) _labels.TryAdd(label.Id, label);
              var cardMap = sourceCards.Select((item, index) => (item.Id, Copy: cards[index].Id)).ToDictionary(item => item.Id, item => item.Copy);
              foreach (var association in associations) _cardLabels.Add((cardMap[association.CardId], labelMap[association.LabelId].Id));
            foreach (var checklist in _checklists.Values.Where(item => item.OrganizationId == source.OrganizationId && sourceIds.Contains(item.CardId) && item.DeletedAt is null).ToArray())
            {
                var child = checklist with { Id = Guid.NewGuid(), CardId = cardMap[checklist.CardId], CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1 };
                _checklists.Add(child.Id, child);
                foreach (var item in _checklistItems.Values.Where(item => item.OrganizationId == source.OrganizationId && item.ChecklistId == checklist.Id && item.DeletedAt is null).ToArray())
                {
                    var copy = item with { Id = Guid.NewGuid(), ChecklistId = child.Id, Completed = false, CompletedAt = null, CompletedBy = null, CreatedAt = createdAt, UpdatedAt = createdAt, Version = 1 };
                    _checklistItems.Add(copy.Id, copy);
                }
            }
            return Task.FromResult(copied);
        }
    }

    public Task<BoardListRecord?> FindListAsync(
        Guid listId,
        CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        lock (_sync)
        {
            _lists.TryGetValue(listId, out var list);
            return Task.FromResult<BoardListRecord?>(includeDeleted || list?.LifecycleState != WorkItemLifecycleState.Deleted ? list : null);
        }
    }

    public Task<long> CountContainedCardsAsync(Guid listId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_lists.TryGetValue(listId, out var list) ? _cards.Values.LongCount(c =>
                c.OrganizationId == list.OrganizationId && c.BoardId == list.BoardId && c.ListId == listId
                && c.LifecycleState != WorkItemLifecycleState.Deleted) : 0);
        }
    }

    public Task<BoardListRecord?> UpdateListAsync(
        Guid listId,
        string name,
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? beforeListId = null, bool moveToEnd = false)
    {
        lock (_sync)
        {
            if (!_lists.TryGetValue(listId, out var list) ||
                list.Version != expectedVersion)
            {
                return Task.FromResult<BoardListRecord?>(null);
            }

            if (beforeListId is not null || moveToEnd)
            {
                var siblings = _lists.Values.Where(value => value.BoardId == list.BoardId && value.Id != listId
                    && value.LifecycleState == WorkItemLifecycleState.Active).OrderBy(value => value.Rank, StringComparer.Ordinal).ThenBy(value => value.Id).ToArray();
                if (beforeListId is not null)
                {
                    var index = Array.FindIndex(siblings, value => value.Id == beforeListId.Value);
                    if (index < 0) return Task.FromResult<BoardListRecord?>(null);
                    var lower = index == 0 ? null : siblings[index - 1].Rank;
                    if (lower == siblings[index].Rank) throw new RankSpaceExhaustedException();
                    rank = RankToken.Between(lower, siblings[index].Rank);
                }
                else rank = RankToken.After(siblings.LastOrDefault()?.Rank);
            }
            var updated = list with
            {
                Name = name,
                Rank = rank,
                UpdatedAt = updatedAt,
                Version = list.Version + 1,
            };

            _lists[listId] = updated;
            return Task.FromResult<BoardListRecord?>(updated);
        }
    }

    public Task<BoardListRecord?> SetListLifecycleAsync(
        Guid listId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        if (nextState == WorkItemLifecycleState.Deleted && (actorUserId is null || actorUserId == Guid.Empty))
            throw new ArgumentException("Deletion requires an actor.", nameof(actorUserId));
        lock (_sync)
        {
            if (!_lists.TryGetValue(listId, out var list) ||
                list.Version != expectedVersion ||
                list.LifecycleState != expectedState)
            {
                return Task.FromResult<BoardListRecord?>(null);
            }

            var updated = list with
            {
                LifecycleState = nextState,
                ArchivedAt = nextState == WorkItemLifecycleState.Archived ? updatedAt : nextState == WorkItemLifecycleState.Active ? null : list.ArchivedAt,
                DeletedAt = nextState == WorkItemLifecycleState.Deleted ? updatedAt : list.DeletedAt,
                DeletedBy = nextState == WorkItemLifecycleState.Deleted ? actorUserId : list.DeletedBy,
                UpdatedAt = updatedAt,
                Version = list.Version + 1,
            };
            _lists[listId] = updated;
            return Task.FromResult<BoardListRecord?>(updated);
        }
    }

    public Task<CardRecord> CreateCardAsync(
        Guid listId,
        Guid cardId,
        string title,
        string? description,
        string? rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_lists.TryGetValue(listId, out var list))
            {
                throw new InvalidOperationException("List was not found.");
            }

            rank ??= RankToken.After(_cards.Values
                .Where(item => item.ListId == listId && item.LifecycleState == WorkItemLifecycleState.Active)
                .Select(item => item.Rank).Order(StringComparer.Ordinal).LastOrDefault());

            var card = new CardRecord(
                cardId,
                list.OrganizationId,
                list.BoardId,
                listId,
                title,
                description,
                rank,
                WorkItemLifecycleState.Active,
                createdAt,
                createdAt,
                1);

            _cards[cardId] = card;
            return Task.FromResult(card);
        }
    }

    public Task<CardRecord?> FindCardAsync(
        Guid cardId,
        CancellationToken cancellationToken = default, bool includeDeleted = false)
    {
        lock (_sync)
        {
            _cards.TryGetValue(cardId, out var card);
            return Task.FromResult<CardRecord?>(card is not null && (includeDeleted || card.LifecycleState != WorkItemLifecycleState.Deleted) ? card : null);
        }
    }

    public Task<CardRecord?> UpdateCardAsync(
        Guid cardId,
        string title,
        string? description,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) ||
                card.Version != expectedVersion)
            {
                return Task.FromResult<CardRecord?>(null);
            }

            var updated = card with
            {
                Title = title,
                Description = description,
                UpdatedAt = updatedAt,
                Version = card.Version + 1,
            };

            _cards[cardId] = updated;
            return Task.FromResult<CardRecord?>(updated);
        }
    }

    public async Task<CardRecord?> MoveCardAsync(
        Guid cardId,
        Guid destinationListId,
        string? rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? beforeCardId = null, bool requireVerifiedEmail = false)
    {
        CardRecord? initial; BoardListRecord? target; Guid[] members;
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out initial) || !_lists.TryGetValue(destinationListId, out target)
                || initial.OrganizationId != target.OrganizationId || initial.Version != expectedVersion) return null;
            members = _cardMembers.Keys.Where(k => k.CardId == cardId).Select(k => k.UserId).ToArray();
        }
        var eligible = new HashSet<Guid>();
        if (initial.BoardId != target.BoardId)
        {
            if (!transactionScope.Owns(initial.OrganizationId)) throw new InvalidOperationException("Cross-Board move requires its owning transaction.");
            foreach (var user in members)
                if (await IsAssignableBoardMemberAsync(target.BoardId, user, requireVerifiedEmail, cancellationToken)) eligible.Add(user);
        }
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) ||
                !_lists.TryGetValue(destinationListId, out var destination) ||
                card.Version != expectedVersion ||
                destination.OrganizationId != card.OrganizationId || card.BoardId != initial.BoardId || destination.BoardId != target.BoardId)
            {
                return null;
            }

            var siblings = _cards.Values.Where(sibling => sibling.Id != cardId && sibling.ListId == destinationListId
                && sibling.LifecycleState == WorkItemLifecycleState.Active).OrderBy(sibling => sibling.Rank, StringComparer.Ordinal).ThenBy(sibling => sibling.Id).ToArray();
            if (beforeCardId is not null)
            {
                var position = Array.FindIndex(siblings, sibling => sibling.Id == beforeCardId);
                if (position < 0) return null;
                if (position > 0 && siblings[position - 1].Rank == siblings[position].Rank) throw new RankSpaceExhaustedException();
                rank = RankToken.Between(position == 0 ? null : siblings[position - 1].Rank, siblings[position].Rank);
            }
            var updated = card with
            {
                BoardId = destination.BoardId,
                ListId = destinationListId,
                Rank = rank ?? RankToken.After(siblings.LastOrDefault()?.Rank),
                UpdatedAt = updatedAt,
                Version = card.Version + 1,
            };
            if (card.BoardId != destination.BoardId)
            {
                var last = _labels.Values.Where(l => l.BoardId == destination.BoardId && !l.Deleted)
                    .Select(l => l.Rank).Order(StringComparer.Ordinal).LastOrDefault();
                var copied = new List<BoardLabelRecord>();
                foreach (var label in _cardLabels.Where(a => a.CardId == cardId).Select(a => _labels[a.LabelId])
                    .Where(l => !l.Deleted).OrderBy(l => l.Rank, StringComparer.Ordinal).ThenBy(l => l.Id))
                {
                    last = RankToken.After(last);
                    copied.Add(label with { Id = Guid.NewGuid(), BoardId = destination.BoardId, Rank = last,
                        CreatedAt = updatedAt, UpdatedAt = updatedAt, Version = 1 });
                }
                _cardLabels.RemoveWhere(a => a.CardId == cardId);
                foreach (var label in copied) { _labels.Add(label.Id, label); _cardLabels.Add((cardId, label.Id)); }
                foreach (var member in _cardMembers.Keys.Where(k => k.CardId == cardId && !eligible.Contains(k.UserId)).ToArray())
                    _cardMembers.Remove(member);
            }
            _cards[cardId] = updated;
            return updated;
        }
    }

    public Task<CardRecord?> SetCardLifecycleAsync(
        Guid cardId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default, Guid? actorUserId = null)
    {
        if (nextState == WorkItemLifecycleState.Deleted && (actorUserId is null || actorUserId == Guid.Empty))
            throw new ArgumentException("Deletion requires an actor.", nameof(actorUserId));
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) ||
                card.Version != expectedVersion ||
                card.LifecycleState != expectedState)
            {
                return Task.FromResult<CardRecord?>(null);
            }

            var updated = card with
            {
                LifecycleState = nextState,
                ArchivedAt = nextState == WorkItemLifecycleState.Archived ? updatedAt : nextState == WorkItemLifecycleState.Active ? null : card.ArchivedAt,
                DeletedAt = nextState == WorkItemLifecycleState.Deleted ? updatedAt : card.DeletedAt,
                DeletedBy = nextState == WorkItemLifecycleState.Deleted ? actorUserId : card.DeletedBy,
                UpdatedAt = updatedAt,
                Version = card.Version + 1,
            };
            _cards[cardId] = updated;
            return Task.FromResult<CardRecord?>(updated);
        }
    }

    public Task AppendAuditAsync(
        Guid organizationId,
        Guid actorUserId,
        string eventType,
        string entityType,
        Guid entityId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
