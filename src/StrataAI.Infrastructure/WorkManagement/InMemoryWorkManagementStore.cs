using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed class InMemoryWorkManagementStore : IWorkManagementStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, BoardRecord> _boards = [];
    private readonly Dictionary<Guid, BoardListRecord> _lists = [];
    private readonly Dictionary<Guid, CardRecord> _cards = [];
    private readonly Dictionary<(Guid BoardId, Guid UserId), BoardMemberRecord> _members = [];
    private readonly HashSet<(Guid BoardId, Guid UserId)> _starred = [];

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
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _boards.TryGetValue(boardId, out var board);
            return Task.FromResult<BoardRecord?>(board);
        }
    }

    public Task<BoardMemberRecord?> FindBoardMemberAsync(
        Guid boardId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _members.TryGetValue((boardId, userId), out var member);
            return Task.FromResult<BoardMemberRecord?>(member);
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
                                .ToArray()))
                .ToArray();

            var starred =
                userId.HasValue && _starred.Contains((boardId, userId.Value));

            return Task.FromResult<BoardSnapshot?>(
                new BoardSnapshot(board, lists, starred, access));
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
        CancellationToken cancellationToken = default)
    {
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
            if (starred)
            {
                _starred.Add((boardId, userId));
            }
            else
            {
                _starred.Remove((boardId, userId));
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BoardMemberRecord>> ListBoardMembersAsync(
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var result = _members.Values
                .Where(member => member.BoardId == boardId && member.Active)
                .OrderBy(member => member.UserId)
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
        string rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_boards.TryGetValue(boardId, out var board))
            {
                throw new InvalidOperationException("Board was not found.");
            }

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

    public Task<BoardListRecord?> FindListAsync(
        Guid listId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _lists.TryGetValue(listId, out var list);
            return Task.FromResult<BoardListRecord?>(list);
        }
    }

    public Task<BoardListRecord?> UpdateListAsync(
        Guid listId,
        string name,
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_lists.TryGetValue(listId, out var list) ||
                list.Version != expectedVersion)
            {
                return Task.FromResult<BoardListRecord?>(null);
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
        CancellationToken cancellationToken = default)
    {
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
        string rank,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_lists.TryGetValue(listId, out var list))
            {
                throw new InvalidOperationException("List was not found.");
            }

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
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _cards.TryGetValue(cardId, out var card);
            return Task.FromResult<CardRecord?>(card);
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

    public Task<CardRecord?> MoveCardAsync(
        Guid cardId,
        Guid destinationListId,
        string rank,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(cardId, out var card) ||
                !_lists.TryGetValue(destinationListId, out var destination) ||
                card.Version != expectedVersion ||
                destination.BoardId != card.BoardId)
            {
                return Task.FromResult<CardRecord?>(null);
            }

            var updated = card with
            {
                ListId = destinationListId,
                Rank = rank,
                UpdatedAt = updatedAt,
                Version = card.Version + 1,
            };

            _cards[cardId] = updated;
            return Task.FromResult<CardRecord?>(updated);
        }
    }

    public Task<CardRecord?> SetCardLifecycleAsync(
        Guid cardId,
        WorkItemLifecycleState expectedState,
        WorkItemLifecycleState nextState,
        long expectedVersion,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
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
