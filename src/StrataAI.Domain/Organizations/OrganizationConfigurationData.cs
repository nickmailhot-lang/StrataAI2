using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StrataAI.Domain.Organizations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationEmergencyContact(string Name, string? Email = null, string? Phone = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationJurisdictionPolicy(string Key, string Value, string Source, string? Notes = null);

// Values describe reviewed tenant configuration; they confer neither access nor legal compliance.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationConfigurationData(
    string LegalName, string Jurisdiction, string Timezone,
    string? CorporationIdentifier = null, string? CivicAddress = null,
    int? LotCount = null, int? FiscalYearEndMonth = null, int? FiscalYearEndDay = null,
    int? AgmCycleMonths = null, int? DepreciationReportCycleMonths = null,
    DateOnly? InsuranceRenewalDate = null, string? ManagementCompanyName = null,
    IReadOnlyList<OrganizationEmergencyContact>? EmergencyContacts = null,
    IReadOnlyList<string>? DefaultCategories = null, IReadOnlyList<string>? DefaultPriorities = null,
    Guid? IntakeBoardId = null, Guid? IntakeListId = null,
    IReadOnlyList<OrganizationJurisdictionPolicy>? JurisdictionPolicies = null);

public static class OrganizationConfigurationRules
{
    public const int MaximumPayloadBytes = 65_536;
    public const int MaximumCollectionSize = 32;

    // Returns a public field name, never the rejected value or a protected relationship.
    public static string? Validate(OrganizationConfigurationData? data)
    {
        if (data is null) return "configuration";
        if (!Required(data.LegalName, 200)) return nameof(data.LegalName);
        if (!Required(data.Jurisdiction, 120)) return nameof(data.Jurisdiction);
        if (!Required(data.Timezone, 128) || data.Timezone != "UTC"
            && !TimeZoneInfo.TryConvertIanaIdToWindowsId(data.Timezone, out _)) return nameof(data.Timezone);
        if (!Optional(data.CorporationIdentifier, 120)) return nameof(data.CorporationIdentifier);
        if (!Optional(data.CivicAddress, 1_000, multiline: true)) return nameof(data.CivicAddress);
        if (!Optional(data.ManagementCompanyName, 200)) return nameof(data.ManagementCompanyName);
        if (data.LotCount is <= 0 or > 1_000_000) return nameof(data.LotCount);
        if (data.FiscalYearEndMonth.HasValue != data.FiscalYearEndDay.HasValue) return "FiscalYearEnd";
        if (data.FiscalYearEndMonth is int month && data.FiscalYearEndDay is int day
            && (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))) return "FiscalYearEnd";
        if (data.AgmCycleMonths is <= 0 or > 1_200) return nameof(data.AgmCycleMonths);
        if (data.DepreciationReportCycleMonths is <= 0 or > 1_200) return nameof(data.DepreciationReportCycleMonths);
        if (data.IntakeBoardId == Guid.Empty || data.IntakeListId == Guid.Empty
            || data.IntakeListId.HasValue && !data.IntakeBoardId.HasValue) return "Intake";
        if (!Labels(data.DefaultCategories)) return nameof(data.DefaultCategories);
        if (!Labels(data.DefaultPriorities)) return nameof(data.DefaultPriorities);
        if (data.EmergencyContacts is { } contacts && (contacts.Count > MaximumCollectionSize
            || contacts.Any(contact => contact is null || !Required(contact.Name, 160)
                || !Optional(contact.Phone, 80) || !Optional(contact.Email, 320)
                || string.IsNullOrWhiteSpace(contact.Email) && string.IsNullOrWhiteSpace(contact.Phone)
                || contact.Email is { Length: > 0 } email && (!MailAddress.TryCreate(email, out var parsed)
                    || parsed.DisplayName.Length != 0 || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)))))
            return nameof(data.EmergencyContacts);
        if (data.JurisdictionPolicies is { } policies && (policies.Count > MaximumCollectionSize
            || policies.Any(policy => policy is null || !Required(policy.Key, 64)
                || !policy.Key.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
                || !Required(policy.Value, 2_000, multiline: true) || !Required(policy.Source, 1_000, multiline: true)
                || !Optional(policy.Notes, 2_000, multiline: true))
            || policies.Select(policy => policy.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policies.Count))
            return nameof(data.JurisdictionPolicies);
        return JsonSerializer.SerializeToUtf8Bytes(data).Length > MaximumPayloadBytes ? "configuration" : null;
    }

    private static bool Required(string? value, int maximum, bool multiline = false) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && !value.Any(character => char.IsControl(character) && !(multiline && character is '\n' or '\r' or '\t'));

    private static bool Optional(string? value, int maximum, bool multiline = false) =>
        value is null || Required(value, maximum, multiline);

    private static bool Labels(IReadOnlyList<string>? values) => values is null
        || values.Count <= MaximumCollectionSize && values.All(value => Required(value, 160))
            && values.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;
}
