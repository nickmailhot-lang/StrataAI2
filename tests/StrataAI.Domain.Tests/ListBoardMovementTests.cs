using StrataAI.Domain.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class ListBoardMovementTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    private static BoardList List() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Planning", "a0", Created);
    private static object Snapshot(BoardList list) => new
    {
        list.Id, list.OrganizationId, list.BoardId, list.Name, list.Rank,
        list.CreatedAt, list.UpdatedAt, list.Version,
    };

    [Fact]
    public void PRD_07_LIST_FR_006_MovePreservesIdentityNameAndCreationHistory()
    {
        var list = List(); var id = list.Id; var organization = list.OrganizationId;
        var destination = Guid.NewGuid(); var updated = Created.AddMinutes(1);

        list.MoveToBoard(organization, destination, " b0 ", updated);

        Assert.Equal(id, list.Id); Assert.Equal(organization, list.OrganizationId);
        Assert.Equal(destination, list.BoardId); Assert.Equal("Planning", list.Name);
        Assert.Equal("b0", list.Rank); Assert.Equal(Created, list.CreatedAt);
        Assert.Equal(updated, list.UpdatedAt); Assert.Equal(2, list.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void PRD_07_LIST_FR_006_InvalidRankLeavesCompleteStateUnchanged(string? rank)
    {
        var list = List(); var before = Snapshot(list);
        Assert.Throws<ArgumentException>(() => list.MoveToBoard(list.OrganizationId, Guid.NewGuid(), rank!, Created.AddMinutes(1)));
        Assert.Equal(before, Snapshot(list));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PRD_07_LIST_FR_006_EmptyOrDifferentOrganizationLeavesCompleteStateUnchanged(bool empty)
    {
        var list = List(); var before = Snapshot(list);
        Assert.Throws<ArgumentException>(() => list.MoveToBoard(empty ? Guid.Empty : Guid.NewGuid(), Guid.NewGuid(), "b0", Created.AddMinutes(1)));
        Assert.Equal(before, Snapshot(list));
    }

    [Fact]
    public void PRD_07_LIST_FR_006_EmptyDestinationLeavesCompleteStateUnchanged()
    {
        var list = List(); var before = Snapshot(list);
        Assert.Throws<ArgumentException>(() => list.MoveToBoard(list.OrganizationId, Guid.Empty, "b0", Created.AddMinutes(1)));
        Assert.Equal(before, Snapshot(list));
    }

    [Fact]
    public void PRD_07_LIST_FR_006_BackwardsClockLeavesCompleteStateUnchanged()
    {
        var list = List(); var before = Snapshot(list);
        Assert.Throws<ArgumentOutOfRangeException>(() => list.MoveToBoard(list.OrganizationId, Guid.NewGuid(), "b0", Created.AddSeconds(-1)));
        Assert.Equal(before, Snapshot(list));
    }

    [Fact]
    public void PRD_07_LIST_FR_006_SubsequentMovementAdvancesTheExistingEntityOncePerMove()
    {
        var list = List(); var destination = Guid.NewGuid();
        list.MoveToBoard(list.OrganizationId, Guid.NewGuid(), "b0", Created.AddMinutes(1));
        list.MoveToBoard(list.OrganizationId, destination, "c0", Created.AddMinutes(2));
        Assert.Equal(destination, list.BoardId); Assert.Equal("c0", list.Rank);
        Assert.Equal(3, list.Version); Assert.Equal(Created.AddMinutes(2), list.UpdatedAt);
    }
}
