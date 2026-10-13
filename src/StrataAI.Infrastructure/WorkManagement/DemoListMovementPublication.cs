using StrataAI.Application.WorkManagement;

namespace StrataAI.Infrastructure.WorkManagement;

internal sealed partial class InMemoryWorkManagementStore : IListBoardMovementPublicationStore
{
    public Task<IReadOnlyDictionary<Guid, CardRecord>> ReadCardsAsync(BoardListRecord destination,
        IReadOnlyList<Guid> cardIds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!transactionScope.Owns(destination.OrganizationId))
            throw new InvalidOperationException("List publication requires its owning command transaction.");
        var ids = DemoPublicationIds(cardIds);
        lock (_sync)
        {
            var rows = ids.Where(id => _cards.TryGetValue(id, out var card) && card.OrganizationId == destination.OrganizationId
                && card.BoardId == destination.BoardId && card.ListId == destination.Id).ToDictionary(id => id, id => _cards[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, CardRecord>>(new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, CardRecord>(rows));
        }
    }

    public async Task AppendCardAuditsAsync(BoardListRecord destination, Guid actor, IReadOnlyList<Guid> cardIds,
        string correlationId, CancellationToken ct)
    {
        var ids = DemoPublicationIds(cardIds);
        var rows = await ReadCardsAsync(destination, ids, ct);
        if (rows.Count != ids.Length) throw new InvalidOperationException("List publication audit omitted a Card reference.");
        foreach (var id in ids)
            await AppendAuditAsync(destination.OrganizationId, actor, "CARD_MOVED", "Card", id, correlationId, ct);
    }

    private static Guid[] DemoPublicationIds(IReadOnlyList<Guid> ids)
    {
        var snapshot = ids.ToArray();
        if (snapshot.Any(id => id == Guid.Empty) || snapshot.Distinct().Count() != snapshot.Length)
            throw new ArgumentException("List publication requires distinct nonempty Card references.", nameof(ids));
        return snapshot;
    }
}
