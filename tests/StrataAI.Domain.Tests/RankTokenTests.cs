using StrataAI.Application.WorkManagement;

namespace StrataAI.Domain.Tests;

public sealed class RankTokenTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(5000)]
    public void DefaultAppendsAndPrependsSupportRequiredBoardCapacity(int count)
    {
        var last = RankToken.Initial();
        var first = last;
        for (var index = 1; index < count; index++)
        {
            var next = RankToken.After(last);
            var previous = RankToken.Before(first);
            Assert.True(RankToken.IsValid(next));
            Assert.True(RankToken.IsValid(previous));
            Assert.True(string.CompareOrdinal(last, next) < 0);
            Assert.True(string.CompareOrdinal(previous, first) < 0);
            var between = RankToken.Between(last, next);
            Assert.True(string.CompareOrdinal(last, between) < 0);
            Assert.True(string.CompareOrdinal(between, next) < 0);
            last = next;
            first = previous;
        }
    }

    [Fact]
    public void ExistingBoundaryRanksRetainMidpointFallbackAndExplicitExhaustion()
    {
        Assert.Equal("999999999999999999999999999998",
            RankToken.After("999999999999999999999999999997"));
        Assert.Equal("000000000000000000000000000001",
            RankToken.Before("000000000000000000000000000002"));
        Assert.Throws<RankSpaceExhaustedException>(() =>
            RankToken.After("999999999999999999999999999998"));
        Assert.Throws<RankSpaceExhaustedException>(() =>
            RankToken.Before("000000000000000000000000000001"));
    }

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
