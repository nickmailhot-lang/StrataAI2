using StrataAI.Application.Common;
using StrataAI.Application.Identity;
using StrataAI.Application.Organizations;

namespace StrataAI.Application.WorkManagement;

public sealed record BoardDatePolicyInput(string? Timezone, long Version);
public sealed record BoardDatePolicyChange(BoardRecord Board, bool Changed);
public interface IBoardDatePolicyStore
{
    Task<BoardRecord?> SetDatePolicyAsync(Guid organizationId, Guid boardId, string? timezone,
        long version, DateTimeOffset now, CancellationToken ct);
}

// Display policy is an administrator command on the existing Board gate and
// revision. It does not reinterpret UTC instants or reschedule personal jobs.
public sealed class BoardDatePolicyService(IWorkManagementStore work, IBoardDatePolicyStore policies,
    IWorkBoardAuthorization boards, IOrganizationStore organizations, IWorkManagementUnitOfWork transactions,
    IWorkCommandContext context, ICommandActorAuthorization actors, IClock clock, IWorkEventStore events)
{
    public async Task<WorkOperation<BoardDatePolicyChange>> SetAsync(Guid boardId, Guid actor,
        BoardDatePolicyInput input, string correlationId, CancellationToken ct = default)
    {
        var hint = await work.FindBoardAsync(boardId, ct);
        if (hint is null) return WorkOperation<BoardDatePolicyChange>.Failure("board_not_found");
        return await transactions.ExecuteAsync(hint.OrganizationId,
            WorkCommand.Create(actor, context.IdempotencyKey, "BoardDatePolicy", boardId, input, "board_not_found"),
            async receipt =>
            {
                if (receipt is not null && (receipt.Board.Id != boardId || receipt.Board.OrganizationId != hint.OrganizationId)) return false;
                if (!await work.AcquireCommandScopeAsync(hint.OrganizationId, actor, boardId, ct) ||
                    await organizations.FindOrganizationAsync(hint.OrganizationId, ct) is not { Status: OrganizationStatus.Active } ||
                    await organizations.FindMembershipAsync(hint.OrganizationId, actor, ct) is not { Active: true }) return false;
                var scope = await boards.GetSyncScopeAsync(boardId, actor, ct);
                return scope.Value is { Access.CanAdminister: true, Board.LifecycleState: BoardLifecycleState.Active }
                    && scope.Value.Board.OrganizationId == hint.OrganizationId;
            }, async () =>
            {
                if (input.Version < 1 || !ValidTimezone(input.Timezone))
                    return WorkOperation<BoardDatePolicyChange>.Failure("invalid_board_date_policy");
                var current = await work.FindBoardAsync(boardId, ct);
                if (current is null || current.Version != input.Version) return WorkOperation<BoardDatePolicyChange>.Failure("version_conflict");
                if (current.DateTimezoneOverride == input.Timezone) return WorkOperation<BoardDatePolicyChange>.Success(new(current, false));
                var updated = await policies.SetDatePolicyAsync(hint.OrganizationId, boardId, input.Timezone, input.Version, clock.UtcNow, ct);
                if (updated is null) return WorkOperation<BoardDatePolicyChange>.Failure("version_conflict");
                await work.AppendAuditAsync(hint.OrganizationId, actor, "BOARD_UPDATED", "Board", boardId, correlationId, ct);
                await events.AppendAsync(new(Guid.NewGuid(), hint.OrganizationId, boardId, actor, "BOARD_UPDATED",
                    "Board", boardId, updated.Version, correlationId, clock.UtcNow), ct);
                if (!await actors.VerifyAsync(actor, ct)) return WorkOperation<BoardDatePolicyChange>.Failure("session_unavailable");
                return WorkOperation<BoardDatePolicyChange>.Success(new(updated, true));
            }, ct);
    }

    private static bool ValidTimezone(string? value)
    {
        if (value is null) return true;
        if (value.Length is < 1 or > 100 || value.Trim() != value ||
            value != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(value, out _)) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}
