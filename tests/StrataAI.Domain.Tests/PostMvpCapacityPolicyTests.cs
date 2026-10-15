using System.Reflection;

namespace StrataAI.Domain.Tests;

public sealed class PostMvpCapacityPolicyTests
{
    private static readonly (Type Type, string Method)[] Capacity =
    [
        (typeof(CardLabelTransferPlanTests), nameof(CardLabelTransferPlanTests.PRD_07_LargeContainedReferenceSetKeepsEveryCardAndOneSharedDefinition)),
        (typeof(RankTokenTests), nameof(RankTokenTests.DefaultAppendsAndPrependsSupportRequiredBoardCapacity))
    ];

    [Fact]
    public void OnlyReviewedDomainCapacityMethodsUseThePhaseGate()
    {
        var actual = typeof(PostMvpCapacityPolicyTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods().Where(method => method.GetCustomAttribute<FactAttribute>()?.SkipUnless == "PostMvpLoadEnabled")
                .Select(method => (Type: type, Method: method.Name)))
            .OrderBy(value => value.Type.FullName).ThenBy(value => value.Method).ToArray();
        Assert.Equal(Capacity.OrderBy(value => value.Type.FullName).ThenBy(value => value.Method), actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CapacityDiscoveryRequiresExplicitOptInWithoutExecutingTheWorkload(int index)
    {
        var (type, method) = Capacity[index];
        var attribute = Assert.IsAssignableFrom<FactAttribute>(type.GetMethod(method)!.GetCustomAttribute<FactAttribute>());
        Assert.NotEmpty(attribute.Skip!);
        Assert.False(attribute.Explicit);
        Assert.Equal("PostMvpLoadEnabled", attribute.SkipUnless);
        Assert.Null(attribute.SkipWhen);
        Assert.Null(attribute.SkipType);
        var property = type.GetProperty(attribute.SkipUnless!, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);
        Assert.Equal(typeof(bool), property.PropertyType);
        Assert.Equal(Environment.GetEnvironmentVariable("STRATAAI_POST_MVP_LOAD_TESTS") == "1", property.GetValue(null));
    }

    [Fact]
    public void SmallRankAndLabelCorrectnessMethodsStayUnconditionallyActive()
    {
        Assert.NotNull(typeof(RankTokenTests).GetMethod(nameof(RankTokenTests.DefaultAppendsAndPrependsSupportSmallSequences)));
        foreach (var type in new[] { typeof(RankTokenTests), typeof(CardLabelTransferPlanTests) })
        foreach (var method in type.GetMethods().Where(method => !Capacity.Contains((type, method.Name))))
        {
            if (method.GetCustomAttribute<FactAttribute>() is not { } attribute) continue;
            Assert.Null(attribute.Skip);
            Assert.False(attribute.Explicit);
            Assert.Null(attribute.SkipUnless);
            Assert.Null(attribute.SkipWhen);
        }
    }
}
