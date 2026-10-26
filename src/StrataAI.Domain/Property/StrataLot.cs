namespace StrataAI.Domain.Property;

// A lot's identity is independent of its current label or occupants. Common
// property is an explicit Card scope, never a synthetic directory row.
public sealed record StrataLotData(string UnitIdentifier, string LotIdentifier, string? CivicAddress,
    string? Building, string? Floor, string? Type, string Status);
public sealed record StrataLot(Guid Id, Guid OrganizationId, long Version, StrataLotData Data,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public static class StrataLotRules
{
    public static string? Validate(StrataLotData? data)
    {
        if (data is null) return "data";
        if (!Text(data.UnitIdentifier, 80, true)) return "unit_identifier";
        if (!Text(data.LotIdentifier, 80, true)) return "lot_identifier";
        if (!Text(data.CivicAddress, 240, false)) return "civic_address";
        if (!Text(data.Building, 120, false)) return "building";
        if (!Text(data.Floor, 40, false)) return "floor";
        if (!Text(data.Type, 80, false)) return "type";
        return data.Status is "ACTIVE" or "INACTIVE" ? null : "status";
    }

    // Restrict identifiers to stable, human-readable strings. No silent case,
    // whitespace or Unicode transformations may rewrite a historical label.
    public static bool Text(string? value, int maximum, bool required) => value is null ? !required
        : value.Length is > 0 && value.Length <= maximum && value.Trim() == value
        && !value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
}
