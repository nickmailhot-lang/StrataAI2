namespace StrataAI.Application.WorkManagement;

// Internal participant: the caller owns both Board gates, admission and commit.
public interface IListBoardMovementPublicationStore
{
    Task<IReadOnlyDictionary<Guid, CardRecord>> ReadCardsAsync(BoardListRecord destination,
        IReadOnlyList<Guid> cardIds, CancellationToken ct);
    Task AppendCardAuditsAsync(BoardListRecord destination, Guid actor, IReadOnlyList<Guid> cardIds,
        string correlationId, CancellationToken ct);
}
