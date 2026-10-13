using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class CardLabelTransferPlanTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    private static BoardLabelRecord Label(Guid organization, Guid board, string rank = "a0", bool deleted = false) =>
        new(Guid.NewGuid(), organization, board, "Shared label", "blue", rank, deleted, Created, Created, 1);

    [Fact]
    public void PRD_07_SharedLabelsArePlannedOnceWithoutLosingCardAssociations()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var cards = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var label = Label(org, board);
        var association = new CardLabelTransferAssociation(cards[0], label.Id);
        var plan = CardLabelTransferPlan.Create(org, board, cards, [label, label],
            [association, association, new(cards[1], label.Id)]);
        Assert.Equal(label, Assert.Single(plan.Labels)); Assert.Equal(2, plan.Associations.Count);
        Assert.Equal(cards.Order(), plan.Associations.Select(value => value.CardId));
        Assert.All(plan.Associations, value => Assert.Equal(label.Id, value.LabelId));
    }

    [Fact]
    public void PRD_07_DeletedAndUnreferencedLabelsDoNotBecomeDestinationDefinitions()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var card = Guid.NewGuid();
        var deleted = Label(org, board, deleted: true); var unused = Label(org, board);
        var plan = CardLabelTransferPlan.Create(org, board, [card], [deleted, unused], [new(card, deleted.Id)]);
        Assert.Empty(plan.Labels); Assert.Empty(plan.Associations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PRD_07_WrongOrganizationOrBoardDefinitionRejectsThePlan(bool organization)
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var card = Guid.NewGuid();
        var wrong = Label(organization ? Guid.NewGuid() : org, organization ? board : Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => CardLabelTransferPlan.Create(org, board, [card], [wrong], [new(card, wrong.Id)]));
    }

    [Fact]
    public void PRD_07_UnresolvedLabelAssociationCannotBeSilentlyDropped()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var card = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CardLabelTransferPlan.Create(org, board, [card], [], [new(card, Guid.NewGuid())]));
    }

    [Fact]
    public void PRD_07_AssociationOutsideTheMovingCardSetIsRejected()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var label = Label(org, board);
        Assert.Throws<ArgumentException>(() => CardLabelTransferPlan.Create(org, board, [Guid.NewGuid()], [label], [new(Guid.NewGuid(), label.Id)]));
    }

    [Fact]
    public void PRD_07_ConflictingDefinitionsCannotChooseAnArbitrarySourceVersion()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var label = Label(org, board); var card = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CardLabelTransferPlan.Create(org, board, [card],
            [label, label with { Color = "red" }], [new(card, label.Id)]));
    }

    [Fact]
    public void PRD_07_DefinitionAndAssociationOrderingUsesSourceOrdinalRank()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var card = Guid.NewGuid();
        var lower = Label(org, board, "Z0"); var upper = Label(org, board, "a0");
        var plan = CardLabelTransferPlan.Create(org, board, [card], [upper, lower], [new(card, upper.Id), new(card, lower.Id)]);
        Assert.Equal(new[] { lower.Id, upper.Id }, plan.Labels.Select(label => label.Id));
        Assert.Equal(new[] { lower.Id, upper.Id }, plan.Associations.Select(value => value.LabelId));
    }

    [Fact]
    public void PRD_07_PlanOwnsItsSnapshotInsteadOfRetainingMutableCallerCollections()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var card = Guid.NewGuid(); var label = Label(org, board);
        var labels = new[] { label }; var associations = new[] { new CardLabelTransferAssociation(card, label.Id) };
        var plan = CardLabelTransferPlan.Create(org, board, [card], labels, associations);
        labels[0] = label with { Color = "red" }; associations[0] = new(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(label, Assert.Single(plan.Labels)); Assert.Equal(new(card, label.Id), Assert.Single(plan.Associations));
        Assert.Throws<NotSupportedException>(() => ((IList<BoardLabelRecord>)plan.Labels).Clear());
    }

    [Fact]
    public void PRD_07_EmptySourceGraphProducesAnEmptyPlan()
    {
        var plan = CardLabelTransferPlan.Create(Guid.NewGuid(), Guid.NewGuid(), [], [], []);
        Assert.Empty(plan.Labels); Assert.Empty(plan.Associations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PRD_07_EmptyScopeOrCardIdentityIsRejected(int invalid)
    {
        Assert.Throws<ArgumentException>(() => CardLabelTransferPlan.Create(invalid == 0 ? Guid.Empty : Guid.NewGuid(),
            invalid == 1 ? Guid.Empty : Guid.NewGuid(), [invalid == 2 ? Guid.Empty : Guid.NewGuid()], [], []));
    }

    [Fact]
    public void PRD_07_LargeContainedReferenceSetKeepsEveryCardAndOneSharedDefinition()
    {
        var org = Guid.NewGuid(); var board = Guid.NewGuid(); var label = Label(org, board);
        var cards = Enumerable.Range(0, 100_000).Select(_ => Guid.NewGuid()).ToArray();
        var plan = CardLabelTransferPlan.Create(org, board, cards, [label], cards.Select(card => new CardLabelTransferAssociation(card, label.Id)));
        Assert.Single(plan.Labels); Assert.Equal(cards.Length, plan.Associations.Count);
        Assert.Equal(cards.Length, plan.Associations.Select(value => value.CardId).Distinct().Count());
    }
}
