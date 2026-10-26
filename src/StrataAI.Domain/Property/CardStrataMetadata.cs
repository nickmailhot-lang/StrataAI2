namespace StrataAI.Domain.Property;

public sealed record CardStrataMetadata(string Priority, string Category, string SourceType,
    string Scope, IReadOnlyList<Guid> StrataLotIds);
public sealed record StrataCardOptions(IReadOnlyList<string> PriorityOrder, IReadOnlyList<string> Categories);

// PRD-34: the list's workflow state and assigned people are intentionally not
// inputs. AI suggestions are not authoritative metadata; a human command must
// explicitly confirm values using the eventual versioned Card command.
public static class CardStrataMetadataRules
{
    public static readonly IReadOnlyList<string> Priorities = Array.AsReadOnly(new[] { "URGENT", "HIGH", "NORMAL", "LOW" });
    public static readonly IReadOnlyList<string> DefaultCategories = Array.AsReadOnly(new[] {
        "operations", "maintenance", "project", "bylaw", "finance", "insurance", "governance", "landscaping",
        "security", "owner request", "vendor", "meeting follow-up", "legal" });
    public static readonly IReadOnlyList<string> Sources = Array.AsReadOnly(new[] { "COUNCIL", "STRATA_MANAGER", "OWNER",
        "RESIDENT", "VENDOR", "MEETING", "EMAIL", "INSPECTION", "PROJECT", "INCIDENT", "SYSTEM", "OTHER" });
    public static StrataCardOptions Defaults => new(Priorities, DefaultCategories);

    public static string? ValidateOptions(StrataCardOptions? options)
    {
        if (options?.PriorityOrder is null || options.PriorityOrder.Count != 4
            || !options.PriorityOrder.Order().SequenceEqual(Priorities.Order())) return "priorities";
        if (options.Categories is null || options.Categories.Count is < 1 or > 100
            || options.Categories.Any(x => !StrataLotRules.Text(x, 80, true))
            || options.Categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Categories.Count) return "categories";
        return null;
    }

    public static string? Validate(CardStrataMetadata? metadata, StrataCardOptions? options)
    {
        if (ValidateOptions(options) is not null) return "options";
        if (metadata is null) return "data";
        if (!Priorities.Contains(metadata.Priority)) return "priority";
        if (!options!.Categories.Contains(metadata.Category, StringComparer.Ordinal)) return "category";
        if (!Sources.Contains(metadata.SourceType)) return "source";
        if (metadata.StrataLotIds is null || metadata.StrataLotIds.Count > 1000
            || metadata.StrataLotIds.Any(x => x == Guid.Empty)
            || metadata.StrataLotIds.Distinct().Count() != metadata.StrataLotIds.Count) return "units";
        var count = metadata.StrataLotIds.Count;
        return metadata.Scope switch {
            "UNIT_SPECIFIC" when count == 1 => null,
            "MULTIPLE_UNITS" when count >= 2 => null,
            "COMMON_PROPERTY" or "ALL_UNITS" or "ORGANIZATION_WIDE" when count == 0 => null,
            _ => "scope"
        };
    }

    // Validate BOTH ends before linking. A relationship is not an access grant.
    // The caller must separately enforce current actor/object authority.
    public static bool UnitsBelongToOrganization(Guid organization, CardStrataMetadata metadata, IReadOnlyList<StrataLot> units) =>
        organization != Guid.Empty && units.Count == metadata.StrataLotIds.Count
        && units.Select(u => u.Id).Distinct().Count() == units.Count
        && units.All(u => u.OrganizationId == organization && u.Version > 0 && u.Data.Status == "ACTIVE"
            && StrataLotRules.Validate(u.Data) is null && metadata.StrataLotIds.Contains(u.Id));
}
