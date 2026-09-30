using StrataAI.Domain.Organizations;
using StrataAI.Domain.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class FoundationHierarchyTests
{
    [Fact]
    public void PRD_01_TC_01_CanonicalHierarchyCarriesStableParentIds()
    {
        var createdAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var organization = new Organization(Guid.NewGuid(), "Quail Ridge", createdAt);
        var board = new Board(Guid.NewGuid(), organization.Id, "Council Operations", createdAt);
        var list = new BoardList(
            Guid.NewGuid(),
            organization.Id,
            board.Id,
            "New",
            "a0",
            createdAt);
        var card = new Card(
            Guid.NewGuid(),
            organization.Id,
            board.Id,
            list.Id,
            "Inspect roof",
            "a0",
            createdAt);

        Assert.Equal(organization.Id, board.OrganizationId);
        Assert.Equal(organization.Id, list.OrganizationId);
        Assert.Equal(board.Id, list.BoardId);
        Assert.Equal(organization.Id, card.OrganizationId);
        Assert.Equal(board.Id, card.BoardId);
        Assert.Equal(list.Id, card.ListId);
    }

    [Fact]
    public void PRD_01_TC_03_EmptyOrganizationIdIsRejected()
    {
        var createdAt = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            new Board(Guid.NewGuid(), Guid.Empty, "Board", createdAt));
    }

    [Fact]
    public void PRD_01_TC_08_MutationAdvancesVersionAndTimestamp()
    {
        var createdAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(1);
        var organization = new Organization(Guid.NewGuid(), "Original", createdAt);

        organization.Rename("Updated", updatedAt);

        Assert.Equal(2, organization.Version);
        Assert.Equal(updatedAt, organization.UpdatedAt);
        Assert.Equal("Updated", organization.Name);
    }

    [Fact]
    public void PRD_01_TC_10_TimestampsCannotMoveBackwards()
    {
        var createdAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var organization = new Organization(Guid.NewGuid(), "Original", createdAt);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            organization.Rename("Invalid", createdAt.AddSeconds(-1)));
    }
}
