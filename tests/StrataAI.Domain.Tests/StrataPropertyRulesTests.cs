using StrataAI.Domain.Property;
using Xunit;
namespace StrataAI.Domain.Tests;

public sealed class StrataPropertyRulesTests
{
    private static readonly StrataLotData Unit = new("48", "SL48", null, null, null, null, "ACTIVE");
    [Fact]
    public void PRD_28_LOT_FR_001_002_006_accepts_stable_identifiers_without_ownership() {
        Assert.Null(StrataLotRules.Validate(Unit));
        Assert.Null(StrataLotRules.Validate(Unit with { UnitIdentifier = "48B", Status = "INACTIVE" }));
    }
    [Theory]
    [InlineData("", "unit_identifier")]
    [InlineData(" 48", "unit_identifier")]
    [InlineData("48\n", "unit_identifier")]
    [InlineData("48\u200b", "unit_identifier")]
    public void PRD_28_rejects_ambiguous_identifiers(string label, string expected) => Assert.Equal(expected, StrataLotRules.Validate(Unit with { UnitIdentifier = label }));
    [Fact]
    public void PRD_28_rejects_missing_or_oversized_metadata() {
        Assert.Equal("data", StrataLotRules.Validate(null));
        Assert.Equal("lot_identifier", StrataLotRules.Validate(Unit with { LotIdentifier = "" }));
        Assert.Equal("civic_address", StrataLotRules.Validate(Unit with { CivicAddress = new string('x', 241) }));
        Assert.Equal("status", StrataLotRules.Validate(Unit with { Status = "COMMON_PROPERTY" }));
    }
    [Theory]
    [InlineData("UNIT_SPECIFIC", 1, true)]
    [InlineData("UNIT_SPECIFIC", 0, false)]
    [InlineData("UNIT_SPECIFIC", 2, false)]
    [InlineData("MULTIPLE_UNITS", 2, true)]
    [InlineData("MULTIPLE_UNITS", 1, false)]
    [InlineData("COMMON_PROPERTY", 0, true)]
    [InlineData("COMMON_PROPERTY", 1, false)]
    [InlineData("ALL_UNITS", 0, true)]
    [InlineData("ALL_UNITS", 1, false)]
    [InlineData("ORGANIZATION_WIDE", 0, true)]
    [InlineData("FAKE", 0, false)]
    public void PRD_28_34_LOT_FR_003_004_005_scope_requires_exact_relationship_shape(string scope, int count, bool valid) {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var metadata = new CardStrataMetadata("NORMAL", "maintenance", "COUNCIL", scope, ids);
        Assert.Equal(valid, CardStrataMetadataRules.Validate(metadata, CardStrataMetadataRules.Defaults) is null);
    }
    [Fact]
    public void PRD_34_SCARD_FR_001_002_003_006_configured_options_and_source_are_independent_of_workflow() {
        var options = new StrataCardOptions(["LOW", "NORMAL", "HIGH", "URGENT"], ["Roof repairs"]);
        var metadata = new CardStrataMetadata("HIGH", "Roof repairs", "INSPECTION", "COMMON_PROPERTY", []);
        Assert.Null(CardStrataMetadataRules.Validate(metadata, options));
        Assert.Equal("category", CardStrataMetadataRules.Validate(metadata, CardStrataMetadataRules.Defaults));
        Assert.Equal("source", CardStrataMetadataRules.Validate(metadata with { SourceType = "AI_GUESS" }, options));
        Assert.Equal("priority", CardStrataMetadataRules.Validate(metadata with { Priority = "DONE" }, options));
        Assert.Equal("categories", CardStrataMetadataRules.ValidateOptions(options with { Categories = ["Roof", "roof"] }));
        Assert.Equal("priorities", CardStrataMetadataRules.ValidateOptions(options with { PriorityOrder = ["HIGH", "HIGH", "LOW", "NORMAL"] }));
    }
    [Fact]
    public void PRD_28_34_cross_tenant_missing_duplicate_and_inactive_unit_links_are_refused() {
        var org = Guid.NewGuid(); var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var metadata = new CardStrataMetadata("NORMAL", "maintenance", "COUNCIL", "UNIT_SPECIFIC", [id]);
        var unit = new StrataLot(id, org, 1, Unit, now, now);
        Assert.True(CardStrataMetadataRules.UnitsBelongToOrganization(org, metadata, [unit]));
        Assert.False(CardStrataMetadataRules.UnitsBelongToOrganization(Guid.NewGuid(), metadata, [unit]));
        Assert.False(CardStrataMetadataRules.UnitsBelongToOrganization(org, metadata, []));
        Assert.False(CardStrataMetadataRules.UnitsBelongToOrganization(org, metadata, [unit with { Data = Unit with { Status = "INACTIVE" } }]));
        Assert.Equal("units", CardStrataMetadataRules.Validate(metadata with { StrataLotIds = [id, id] }, CardStrataMetadataRules.Defaults));
        Assert.Equal("units", CardStrataMetadataRules.Validate(metadata with { StrataLotIds = [Guid.Empty] }, CardStrataMetadataRules.Defaults));
    }
}
