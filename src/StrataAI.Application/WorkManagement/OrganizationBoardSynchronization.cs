namespace StrataAI.Application.WorkManagement;

public interface IOrganizationBoardEventReader
{
    Task<OrganizationBoardCursorBinding?> GetScopeAsync(Guid organizationId, Guid actorId, CancellationToken cancellationToken = default);
    Task<long> GetHeadAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<WorkOperation<OrganizationBoardEventPage>> ReadAsync(Guid organizationId, Guid actorId, long since, int limit,
        CancellationToken cancellationToken = default);
}
public sealed record OrganizationBoardSyncEvent(Guid EventId, Guid BoardId, string EventType, long Version, DateTimeOffset CreatedAt);
public sealed record OrganizationBoardSyncPage(string Cursor, bool HasMore, bool Pending, bool ResetRequired,
    IReadOnlyList<OrganizationBoardSyncEvent> Events);

// Coordinator foundation. A transport must additionally own the Work read
// transaction and revalidate the authenticated session before disclosing pages.
public sealed class OrganizationBoardSynchronizationService(IOrganizationBoardEventReader reader, IOrganizationBoardCursorCodec cursors)
{
    public async Task<WorkOperation<bool>> IsCursorCurrentAsync(Guid organizationId, Guid actorId, string cursor,
        CancellationToken cancellationToken = default)
    {
        var scope = await reader.GetScopeAsync(organizationId, actorId, cancellationToken);
        if (scope is null || scope.OrganizationId != organizationId || scope.ActorId != actorId)
            return WorkOperation<bool>.Failure("organization_not_found");
        return WorkOperation<bool>.Success(cursors.TryDecode(scope, cursor, out _));
    }
    public async Task<WorkOperation<OrganizationBoardSyncPage>> ReadAsync(Guid organizationId, Guid actorId,
        string? cursor, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || actorId == Guid.Empty)
            return WorkOperation<OrganizationBoardSyncPage>.Failure("organization_not_found");
        if (limit is < 1 or > 100) return WorkOperation<OrganizationBoardSyncPage>.Failure("invalid_sync_limit");
        var scope = await reader.GetScopeAsync(organizationId, actorId, cancellationToken);
        if (scope is null || scope.OrganizationId != organizationId || scope.ActorId != actorId)
            return WorkOperation<OrganizationBoardSyncPage>.Failure("organization_not_found");
        long position = 0;
        OrganizationBoardEventPage page;
        var reset = cursor is null || !cursors.TryDecode(scope, cursor, out position);
        if (reset)
            page = new(await reader.GetHeadAsync(organizationId, cancellationToken), false, false, true, []);
        else
        {
            var replay = await reader.ReadAsync(organizationId, actorId, position, limit, cancellationToken);
            if (!replay.Succeeded || replay.Value is null)
                return WorkOperation<OrganizationBoardSyncPage>.Failure(replay.ErrorCode ?? "organization_sync_unavailable");
            page = replay.Value;
            if (page.ResetRequired)
                page = new(await reader.GetHeadAsync(organizationId, cancellationToken), false, false, true, []);
        }
        // Empty/reset pages need the same final permission proof as event pages.
        var current = await reader.GetScopeAsync(organizationId, actorId, cancellationToken);
        if (current is null || current.OrganizationId != organizationId || current.ActorId != actorId)
            return WorkOperation<OrganizationBoardSyncPage>.Failure("organization_not_found");
        if (current != scope)
            return WorkOperation<OrganizationBoardSyncPage>.Failure("organization_sync_unavailable");
        if (page.Cursor < 0 || page.Events.Count > limit || page.Events.Any(e => !e.Ready || e.EventId == Guid.Empty ||
            e.BoardId == Guid.Empty || e.Version < 1 || !Canonical(e.EventType)))
            return WorkOperation<OrganizationBoardSyncPage>.Failure("organization_sync_unavailable");
        var events = page.Events.Select(e => new OrganizationBoardSyncEvent(e.EventId, e.BoardId, e.EventType, e.Version, e.CreatedAt)).ToArray();
        return WorkOperation<OrganizationBoardSyncPage>.Success(new(cursors.Encode(current, page.Cursor),
            page.HasMore, page.Pending, page.ResetRequired, events));
    }
    private static bool Canonical(string type) => type is "BOARD_CREATED" or "BOARD_UPDATED" or "BOARD_COPIED" or
        "BOARD_ARCHIVED" or "BOARD_RESTORED" or "BOARD_DELETED";
}
