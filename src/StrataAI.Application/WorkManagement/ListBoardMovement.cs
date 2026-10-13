namespace StrataAI.Application.WorkManagement;

public sealed record ListTransferredCard(Guid Id, long SourceVersion, WorkItemLifecycleState LifecycleState);
public sealed record ListBoardTransfer(BoardListRecord List, IReadOnlyList<ListTransferredCard> Cards);

// Internal owning command contract. The Application caller must admit both
// Board scopes and publish receipts/audit/events in the same transaction.
// This moves every contained Card lifecycle and is not a public authorization API.
public interface IListBoardMovementStore
{
    Task<ListBoardTransfer?> TransferListBoardAsync(Guid organization, Guid sourceBoard, Guid list,
        Guid destinationBoard, long expectedVersion, DateTimeOffset now, bool requireVerifiedEmail,
        Guid? beforeList, CancellationToken ct);
}
