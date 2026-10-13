using System.Text.Json;
using StrataAI.Domain.Common;
using StrataAI.Domain.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class CanonicalMutationAtomicityTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    private static string Snapshot(DomainEntity entity) => JsonSerializer.Serialize(entity, entity.GetType());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void PRD_01_CardMoveInvalidRankPreservesCompleteCanonicalState(string? rank)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Planning", "a0", Created);
        var before = Snapshot(card);
        Assert.Throws<ArgumentException>(() => card.Move(Guid.NewGuid(), Guid.NewGuid(), rank!, Created.AddMinutes(1)));
        Assert.Equal(before, Snapshot(card));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("board")]
    [InlineData("list-name")]
    [InlineData("list-rank")]
    [InlineData("card-title")]
    [InlineData("card-move")]
    [InlineData("list-board")]
    public void PRD_01_BackwardsClockPreservesEveryCanonicalMutationField(string operation)
    {
        var mutation = Mutation(operation, Created.AddSeconds(-1));
        var before = Snapshot(mutation.Entity);
        Assert.Throws<ArgumentOutOfRangeException>(mutation.Mutate);
        Assert.Equal(before, Snapshot(mutation.Entity));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("board")]
    [InlineData("list-name")]
    [InlineData("list-rank")]
    [InlineData("card-title")]
    [InlineData("card-move")]
    [InlineData("list-board")]
    public void PRD_01_ValidCanonicalMutationStillAppliesFieldsAndAdvancesClockOnce(string operation)
    {
        var updated = Created.AddMinutes(1); var mutation = Mutation(operation, updated);
        var id = mutation.Entity.Id;
        var scope = (mutation.Entity as IOrganizationScoped)?.OrganizationId;
        var board = (mutation.Entity as Card)?.BoardId ?? (mutation.Entity as BoardList)?.BoardId;
        var list = (mutation.Entity as Card)?.ListId;
        mutation.Mutate();
        Assert.Equal(id, mutation.Entity.Id); Assert.Equal(Created, mutation.Entity.CreatedAt);
        Assert.Equal(updated, mutation.Entity.UpdatedAt); Assert.Equal(2, mutation.Entity.Version);
        Assert.Equal(scope, (mutation.Entity as IOrganizationScoped)?.OrganizationId);
        switch (operation)
        {
            case "organization": Assert.Equal("Changed", ((Organization)mutation.Entity).Name); break;
            case "board": Assert.Equal("Changed", ((Board)mutation.Entity).Name); break;
            case "list-name": Assert.Equal("Changed", ((BoardList)mutation.Entity).Name); break;
            case "list-rank": Assert.Equal("b0", ((BoardList)mutation.Entity).Rank); break;
            case "card-title": Assert.Equal("Changed", ((Card)mutation.Entity).Title); break;
            case "card-move":
                Assert.NotEqual(board, ((Card)mutation.Entity).BoardId);
                Assert.NotEqual(list, ((Card)mutation.Entity).ListId);
                Assert.Equal("b0", ((Card)mutation.Entity).Rank); break;
            case "list-board":
                Assert.NotEqual(board, ((BoardList)mutation.Entity).BoardId);
                Assert.Equal("b0", ((BoardList)mutation.Entity).Rank); break;
        }
    }

    private static (DomainEntity Entity, Action Mutate) Mutation(string operation, DateTimeOffset timestamp)
    {
        var organization = new Organization(Guid.NewGuid(), "Organization", Created);
        var board = new Board(Guid.NewGuid(), organization.Id, "Board", Created);
        var list = new BoardList(Guid.NewGuid(), organization.Id, board.Id, "List", "a0", Created);
        var card = new Card(Guid.NewGuid(), organization.Id, board.Id, list.Id, "Card", "a0", Created);
        return operation switch
        {
            "organization" => (organization, () => organization.Rename("Changed", timestamp)),
            "board" => (board, () => board.Rename("Changed", timestamp)),
            "list-name" => (list, () => list.Rename("Changed", timestamp)),
            "list-rank" => (list, () => list.Reorder("b0", timestamp)),
            "card-title" => (card, () => card.Rename("Changed", timestamp)),
            "card-move" => (card, () => card.Move(Guid.NewGuid(), Guid.NewGuid(), "b0", timestamp)),
            "list-board" => (list, () => list.MoveToBoard(organization.Id, Guid.NewGuid(), "b0", timestamp)),
            _ => throw new ArgumentException("Unknown test mutation"),
        };
    }
}
