namespace StrataAI.Application.WorkManagement;

public sealed partial class WorkManagementService
{
    public async Task<WorkOperation<BoardLabelRecord>> MoveLabelAsync(Guid labelId, Guid actorId, Guid? beforeLabelId, long version, string correlationId, CancellationToken cancellationToken = default)
    {
        var label = await store.FindLabelAsync(labelId, cancellationToken);
        if (label is null) return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        var access = await ResolveAccessAsync(label.BoardId, actorId, cancellationToken);
        if (access is not { Access.CanEdit: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        if (label.Version != version) return WorkOperation<BoardLabelRecord>.Failure("version_conflict");
        if (beforeLabelId is not null)
        {
            var anchor = await store.FindLabelAsync(beforeLabelId.Value, cancellationToken);
            if (anchor is null || anchor.Id == label.Id || anchor.BoardId != label.BoardId || anchor.OrganizationId != label.OrganizationId)
                return WorkOperation<BoardLabelRecord>.Failure("invalid_move_position");
        }
        BoardLabelRecord? moved;
        try { moved = await store.MoveLabelAsync(labelId, beforeLabelId, version, clock.UtcNow, cancellationToken); }
        catch (RankSpaceExhaustedException) { return WorkOperation<BoardLabelRecord>.Failure("rank_space_exhausted"); }
        if (moved is null) return WorkOperation<BoardLabelRecord>.Failure("version_conflict");
        await RecordChangeAsync(moved.OrganizationId, moved.BoardId, actorId, "LABEL_UPDATED", "Label", moved.Id, moved.Version, correlationId, cancellationToken);
        return WorkOperation<BoardLabelRecord>.Success(moved);
    }
    private static readonly HashSet<string> LabelColors = new(StringComparer.Ordinal)
        { "green", "yellow", "orange", "red", "purple", "blue", "sky", "lime", "pink", "black" };
    public async Task<WorkOperation<BoardLabelPage>> ListLabelsAsync(Guid boardId, Guid? actorId, Guid? after = null, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (access is not { Access.CanView: true }) return WorkOperation<BoardLabelPage>.Failure("board_not_found");
        if (after == Guid.Empty) return WorkOperation<BoardLabelPage>.Failure("invalid_label_cursor");
        var rows = await store.ListLabelsAsync(boardId, after, cancellationToken); var items = rows.Take(50).ToArray();
        var active = access.Value.Board.LifecycleState == BoardLifecycleState.Active;
        return WorkOperation<BoardLabelPage>.Success(new(access.Value.Board.OrganizationId, boardId, items,
            rows.Count > 50 ? items[^1].Id : null, active && access.Value.Access.CanEdit, active && access.Value.Access.CanAdminister));
    }
    public async Task<WorkOperation<BoardLabelRecord>> CreateLabelAsync(Guid boardId, Guid actorId, string name, string color,
        string correlationId, CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(boardId, actorId, cancellationToken);
        if (access is not { Access.CanEdit: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<BoardLabelRecord>.Failure("board_not_found");
        var normalizedName = name?.Trim(); var normalizedColor = color?.Trim().ToLowerInvariant();
        if (normalizedName is null || normalizedName.Length > 160) return WorkOperation<BoardLabelRecord>.Failure("invalid_label_name");
        if (normalizedColor is null || !LabelColors.Contains(normalizedColor)) return WorkOperation<BoardLabelRecord>.Failure("invalid_label_color");
        BoardLabelRecord label;
        try { label = await store.CreateLabelAsync(boardId, Guid.NewGuid(), normalizedName, normalizedColor, clock.UtcNow, cancellationToken); }
        catch (RankSpaceExhaustedException) { return WorkOperation<BoardLabelRecord>.Failure("rank_space_exhausted"); }
        await RecordChangeAsync(label.OrganizationId, boardId, actorId, "LABEL_CREATED", "Label", label.Id, label.Version, correlationId, cancellationToken);
        return WorkOperation<BoardLabelRecord>.Success(label);
    }
    public async Task<WorkOperation<BoardLabelRecord>> UpdateLabelAsync(Guid labelId, Guid actorId, string name, string color,
        string? rank, long version, string correlationId, CancellationToken cancellationToken = default)
    {
        var label = await store.FindLabelAsync(labelId, cancellationToken);
        if (label is null) return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        var access = await ResolveAccessAsync(label.BoardId, actorId, cancellationToken);
        if (access is not { Access.CanEdit: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        if (label.Version != version) return WorkOperation<BoardLabelRecord>.Failure("version_conflict");
        var normalizedName = name?.Trim(); var normalizedColor = color?.Trim().ToLowerInvariant();
        if (normalizedName is null || normalizedName.Length > 160) return WorkOperation<BoardLabelRecord>.Failure("invalid_label_name");
        if (normalizedColor is null || !LabelColors.Contains(normalizedColor)) return WorkOperation<BoardLabelRecord>.Failure("invalid_label_color");
        rank ??= label.Rank;
        if (!RankToken.IsValid(rank)) return WorkOperation<BoardLabelRecord>.Failure("invalid_rank");
        var updated = await store.UpdateLabelAsync(labelId, normalizedName, normalizedColor, rank, version, clock.UtcNow, cancellationToken);
        if (updated is null) return WorkOperation<BoardLabelRecord>.Failure("version_conflict");
        await RecordChangeAsync(updated.OrganizationId, updated.BoardId, actorId, "LABEL_UPDATED", "Label", updated.Id, updated.Version, correlationId, cancellationToken);
        return WorkOperation<BoardLabelRecord>.Success(updated);
    }
    public async Task<WorkOperation<BoardLabelRecord>> DeleteLabelAsync(Guid labelId, Guid actorId, long version,
        bool confirmed, string correlationId, CancellationToken cancellationToken = default)
    {
        var label = await store.FindLabelAsync(labelId, cancellationToken);
        if (label is null) return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        var access = await ResolveAccessAsync(label.BoardId, actorId, cancellationToken);
        if (access is not { Access.CanAdminister: true } || access.Value.Board.LifecycleState != BoardLifecycleState.Active)
            return WorkOperation<BoardLabelRecord>.Failure("label_not_found");
        if (!confirmed) return WorkOperation<BoardLabelRecord>.Failure("delete_confirmation_required");
        if (label.Version != version) return WorkOperation<BoardLabelRecord>.Failure("version_conflict");
        var deleted = await store.DeleteLabelAsync(labelId, version, clock.UtcNow, cancellationToken);
        if (deleted is null) return WorkOperation<BoardLabelRecord>.Failure("version_conflict");
        await RecordChangeAsync(deleted.OrganizationId, deleted.BoardId, actorId, "LABEL_DELETED", "Label", deleted.Id, deleted.Version, correlationId, cancellationToken);
        return WorkOperation<BoardLabelRecord>.Success(deleted);
    }
}
