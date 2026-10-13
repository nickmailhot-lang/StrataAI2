namespace StrataAI.Application.WorkManagement;

public sealed record CardLabelTransferAssociation(Guid CardId, Guid LabelId);

// A pre-mutation source snapshot, not an authorization or destination grant.
public sealed class CardLabelTransferPlan
{
    private CardLabelTransferPlan(BoardLabelRecord[] labels, CardLabelTransferAssociation[] associations)
    {
        Labels = Array.AsReadOnly(labels);
        Associations = Array.AsReadOnly(associations);
    }

    public IReadOnlyList<BoardLabelRecord> Labels { get; }
    public IReadOnlyList<CardLabelTransferAssociation> Associations { get; }

    public static CardLabelTransferPlan Create(Guid organizationId, Guid sourceBoardId,
        IReadOnlyCollection<Guid> cardIds, IEnumerable<BoardLabelRecord> labels,
        IEnumerable<CardLabelTransferAssociation> associations)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(associations);
        if (organizationId == Guid.Empty || sourceBoardId == Guid.Empty || cardIds.Contains(Guid.Empty))
            throw new ArgumentException("A current source scope is required.");
        var cards = cardIds.ToHashSet();
        var definitions = new Dictionary<Guid, BoardLabelRecord>();
        foreach (var label in labels)
        {
            if (label.Id == Guid.Empty || label.OrganizationId != organizationId || label.BoardId != sourceBoardId)
                throw new ArgumentException("Label definition is outside the source scope.");
            if (definitions.TryGetValue(label.Id, out var prior) && prior != label)
                throw new ArgumentException("Label source definitions conflict.");
            definitions[label.Id] = label;
        }
        var retained = new HashSet<CardLabelTransferAssociation>();
        foreach (var association in associations)
        {
            if (!cards.Contains(association.CardId) || !definitions.TryGetValue(association.LabelId, out var label))
                throw new ArgumentException("Label association is outside the source snapshot.");
            if (!label.Deleted) retained.Add(association);
        }
        var referenced = retained.Select(value => value.LabelId).ToHashSet();
        var ordered = definitions.Values.Where(label => referenced.Contains(label.Id))
            .OrderBy(label => label.Rank, StringComparer.Ordinal).ThenBy(label => label.Id).ToArray();
        var order = ordered.Select((label, ordinal) => (label.Id, ordinal)).ToDictionary(value => value.Id, value => value.ordinal);
        return new(ordered, retained.OrderBy(value => value.CardId).ThenBy(value => order[value.LabelId]).ToArray());
    }
}
