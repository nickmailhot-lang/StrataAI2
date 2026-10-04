using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record GlobalSearchPage(IReadOnlyList<SearchCardDocument> Items, string? NextCursor);
public sealed class GlobalSearchService(IOrganizationStore organizations, IWorkManagementStore store,
    IWorkManagementService work, IWorkManagementUnitOfWork transactions, ICommandActorAuthorization actors,
    IGlobalSearchCursorCodec cursors)
{
    public async Task<WorkOperation<GlobalSearchPage>> SearchAsync(GlobalSearchBinding binding, string? after,
        CancellationToken ct = default)
    {
        if (binding.ActorId == Guid.Empty || !await actors.VerifyAsync(binding.ActorId, ct))
            return WorkOperation<GlobalSearchPage>.Failure("session_unavailable");
        static bool Text(string? value) => value is not null && value.Length <= 160 && value == value.Trim();
        if (!Text(binding.Keyword) || !Text(binding.Label) || !Text(binding.Member) || !Enum.IsDefined(binding.Scope))
            return WorkOperation<GlobalSearchPage>.Failure("invalid_search");
        GlobalSearchPosition? resume = null;
        if (after is not null && !cursors.TryDecode(binding, after, out resume))
            return WorkOperation<GlobalSearchPage>.Failure("invalid_search");
        async Task<WorkOperation<GlobalSearchPage>> Finish(IReadOnlyList<SearchCardDocument> items, GlobalSearchPosition? position)
            => !await actors.VerifyAsync(binding.ActorId, ct)
                ? WorkOperation<GlobalSearchPage>.Failure("session_unavailable")
                : WorkOperation<GlobalSearchPage>.Success(new(items, position is null ? null : cursors.Encode(binding, position)));

        var organizationReads = 0; var boardReads = 0;
        Guid? organizationAfter = resume?.OrganizationId;
        var pending = new Queue<Guid>();
        if (resume is not null) pending.Enqueue(resume.OrganizationId);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (pending.Count == 0)
            {
                var routes = await organizations.ListMembershipOrganizationIdsPageAsync(binding.ActorId, organizationAfter, ct);
                if (routes.Count == 0) return await Finish([], null);
                foreach (var route in routes.Take(50)) pending.Enqueue(route);
            }
            var organization = pending.Dequeue();
            organizationAfter = organization;
            var current = resume?.OrganizationId == organization ? resume : null;
            organizationReads++;
            Guid? boardAfter = current?.BoardId;
            var admission = await DirectoryAsync(organization, binding.ActorId, boardAfter, ct);
            if (!admission.Succeeded)
            {
                if (admission.ErrorCode != "organization_not_found")
                    return WorkOperation<GlobalSearchPage>.Failure(admission.ErrorCode ?? "session_unavailable");
                if (organizationReads >= 20) return await Finish([], new(organization, null, null));
                continue;
            }
            var pendingBoards = new Queue<Guid>();
            if (current?.BoardId is { } currentBoard) pendingBoards.Enqueue(currentBoard);
            foreach (var board in admission.Value!.Take(50)) pendingBoards.Enqueue(board.Id);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (pendingBoards.Count == 0)
                {
                    var directory = await DirectoryAsync(organization, binding.ActorId, boardAfter, ct);
                    if (!directory.Succeeded)
                    {
                        if (directory.ErrorCode != "organization_not_found") return WorkOperation<GlobalSearchPage>.Failure(directory.ErrorCode ?? "session_unavailable");
                        break;
                    }
                    if (directory.Value!.Count == 0) break;
                    foreach (var board in directory.Value.Take(50)) pendingBoards.Enqueue(board.Id);
                }
                var boardId = pendingBoards.Dequeue(); boardAfter = boardId; boardReads++;
                // Membership hints and directory names never enter the response.
                // Board service locks and freshly admits the actual content.
                var page = await work.SearchBoardAsync(boardId, binding,
                    current?.BoardId == boardId ? current.CardId : null, ct);
                if (!page.Succeeded && page.ErrorCode != "board_not_found")
                    return WorkOperation<GlobalSearchPage>.Failure(page.ErrorCode ?? "session_unavailable");
                if (page.Succeeded && page.Value!.OrganizationId == organization && page.Value.Items.Count > 0)
                    return await Finish(page.Value.Items, new(organization, boardId, page.Value.Items[^1].Card.Id));
                if (boardReads >= 20) return await Finish([], new(organization, boardId, current?.BoardId == boardId ? current.CardId : null));
            }
            if (organizationReads >= 20) return await Finish([], new(organization, null, null));
        }
    }

    private Task<WorkOperation<IReadOnlyList<OrganizationBoardSummary>>> DirectoryAsync(Guid organization, Guid actor, Guid? after, CancellationToken ct)
        => transactions.ExecuteReadAsync(organization, actor, "organization_not_found",
            async () =>
            {
                if (!await store.AcquireOrganizationReadScopeAsync(organization, actor, ct)) return false;
                var parent = await organizations.FindOrganizationAsync(organization, ct);
                var member = await organizations.FindMembershipAsync(organization, actor, ct);
                return parent is { Status: not OrganizationStatus.Deleting } && member is { Active: true };
            }, async () =>
            {
                var member = await organizations.FindMembershipAsync(organization, actor, ct);
                if (member is not { Active: true }) return WorkOperation<IReadOnlyList<OrganizationBoardSummary>>.Failure("organization_not_found");
                var rows = await store.ListVisibleBoardsPageAsync(organization, actor,
                    member.Role is OrganizationRole.Owner or OrganizationRole.Admin, after, ct);
                return WorkOperation<IReadOnlyList<OrganizationBoardSummary>>.Success(rows);
            }, ct);
}
