using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class RankTokenTests
{
    [Fact]
    public void PRD_06_TC_01_RankBetweenSortsLexicographically()
    {
        var first = RankToken.Initial();
        var second = RankToken.After(first);
        var between = RankToken.Between(first, second);

        Assert.True(string.CompareOrdinal(first, between) < 0);
        Assert.True(string.CompareOrdinal(between, second) < 0);
    }

    [Fact]
    public void PRD_06_TC_08_RankUsesFixedWidthAddressSpace()
    {
        var rank = RankToken.Initial();

        Assert.Equal(RankToken.Width, rank.Length);
        Assert.True(RankToken.IsValid(rank));
    }

    [Fact]
    public void PRD_06_TC_12_InvalidSiblingOrderIsRejected()
    {
        var first = RankToken.Initial();
        var second = RankToken.After(first);

        Assert.Throws<ArgumentException>(
            () => RankToken.Between(second, first));
    }
}
