using System.Text.Json;
using StrataAI.Domain.Organizations;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class OrganizationConfigurationTests
{
    private static OrganizationConfigurationData Configuration() => new("Reviewed legal name", "CA-BC", "America/Vancouver");

    [Fact]
    public void PRD_27_Reviewed_configuration_supports_strata_values_and_sourced_policy_without_assuming_law()
    {
        var data = Configuration() with {
            CorporationIdentifier = "REG-123", CivicAddress = "First address line\nSecond address line", LotCount = 48,
            FiscalYearEndMonth = 2, FiscalYearEndDay = 29, AgmCycleMonths = 12, DepreciationReportCycleMonths = 36,
            InsuranceRenewalDate = new DateOnly(2027, 6, 1), ManagementCompanyName = "Reviewed management company",
            EmergencyContacts = [new("Emergency contact", Phone: "Reviewed telephone")],
            DefaultCategories = ["Maintenance", "Insurance"], DefaultPriorities = ["Routine", "Urgent"],
            IntakeBoardId = Guid.NewGuid(), IntakeListId = Guid.NewGuid(),
            JurisdictionPolicies = [new("report-cycle", "Reviewed policy value", "Operator-supplied source", "Reviewed notes")],
        };
        Assert.Null(OrganizationConfigurationRules.Validate(data));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(JsonSerializer.Deserialize<OrganizationConfigurationData>(JsonSerializer.Serialize(data))));
    }

    [Theory]
    [InlineData(0)] [InlineData(13)]
    public void PRD_27_Invalid_fiscal_month_is_rejected_before_calendar_lookup(int month) =>
        Assert.Equal("FiscalYearEnd", OrganizationConfigurationRules.Validate(Configuration() with { FiscalYearEndMonth = month, FiscalYearEndDay = 1 }));

    [Theory]
    [InlineData("", "LegalName")] [InlineData("\u0000", "LegalName")]
    public void PRD_27_Private_invalid_values_return_only_the_public_field(string value, string field) =>
        Assert.Equal(field, OrganizationConfigurationRules.Validate(Configuration() with { LegalName = value }));

    [Fact]
    public void PRD_27_Configuration_rejects_invalid_cycles_calendar_relationships_and_unsourced_policy()
    {
        Assert.Equal("LotCount", OrganizationConfigurationRules.Validate(Configuration() with { LotCount = 0 }));
        Assert.Equal("FiscalYearEnd", OrganizationConfigurationRules.Validate(Configuration() with { FiscalYearEndMonth = 4, FiscalYearEndDay = 31 }));
        Assert.Equal("FiscalYearEnd", OrganizationConfigurationRules.Validate(Configuration() with { FiscalYearEndDay = 31 }));
        Assert.Equal("AgmCycleMonths", OrganizationConfigurationRules.Validate(Configuration() with { AgmCycleMonths = 0 }));
        Assert.Equal("DepreciationReportCycleMonths", OrganizationConfigurationRules.Validate(Configuration() with { DepreciationReportCycleMonths = -1 }));
        Assert.Equal("Intake", OrganizationConfigurationRules.Validate(Configuration() with { IntakeListId = Guid.NewGuid() }));
        Assert.Equal("Intake", OrganizationConfigurationRules.Validate(Configuration() with { IntakeBoardId = Guid.Empty }));
        Assert.Equal("JurisdictionPolicies", OrganizationConfigurationRules.Validate(Configuration() with { JurisdictionPolicies = [new("policy", "value", "")] }));
        Assert.Equal("JurisdictionPolicies", OrganizationConfigurationRules.Validate(Configuration() with { JurisdictionPolicies = [new("policy", "value", "source"), new("POLICY", "value", "source")] }));
        Assert.Equal("EmergencyContacts", OrganizationConfigurationRules.Validate(Configuration() with { EmergencyContacts = [new("Contact")] }));
        Assert.Equal("EmergencyContacts", OrganizationConfigurationRules.Validate(Configuration() with { EmergencyContacts = [new("Contact", "Name <contact@example.test>")] }));
    }

    [Theory]
    [InlineData("UTC")] [InlineData("America/Vancouver")] [InlineData("Europe/London")]
    public void PRD_27_Iana_timezone_configuration_is_explicit(string timezone) =>
        Assert.Null(OrganizationConfigurationRules.Validate(Configuration() with { Timezone = timezone }));

    [Theory]
    [InlineData("Unknown/Zone")] [InlineData("Pacific Standard Time")] [InlineData("")]
    public void PRD_27_Unknown_or_platform_alias_timezones_are_rejected(string timezone) =>
        Assert.Equal("Timezone", OrganizationConfigurationRules.Validate(Configuration() with { Timezone = timezone }));

    [Fact]
    public void PRD_27_Bounded_collections_reject_duplicate_labels_and_oversized_private_payloads()
    {
        Assert.Equal("DefaultCategories", OrganizationConfigurationRules.Validate(Configuration() with { DefaultCategories = ["Routine", " routine "] }));
        Assert.Equal("DefaultPriorities", OrganizationConfigurationRules.Validate(Configuration() with { DefaultPriorities = Enumerable.Range(0, 33).Select(i => $"Priority {i}").ToArray() }));
        Assert.Equal("configuration", OrganizationConfigurationRules.Validate(Configuration() with {
            JurisdictionPolicies = Enumerable.Range(0, 32).Select(i => new OrganizationJurisdictionPolicy($"policy-{i}", new string('v', 2_000), "Reviewed source", new string('n', 2_000))).ToArray(),
        }));
    }

    [Fact]
    public void PRD_27_Unknown_configuration_fields_are_not_silently_ignored() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OrganizationConfigurationData>("{\"LegalName\":\"Reviewed\",\"Jurisdiction\":\"CA-BC\",\"Timezone\":\"UTC\",\"AuthorityGrant\":true}"));
}
