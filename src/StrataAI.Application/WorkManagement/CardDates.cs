using System.Globalization;
using System.Text.RegularExpressions;

namespace StrataAI.Application.WorkManagement;

public sealed record CardDatesInput(string? StartAt, string? DueAt, string? DueTimezone,
    bool DueHasTime, bool DueComplete, long Version);
public sealed record CardDateValues(DateTimeOffset? StartAt, DateTimeOffset? DueAt, string? DueTimezone,
    bool DueHasTime, bool DueComplete)
{
    public static CardDateValues From(CardRecord card) => new(card.StartAt, card.DueAt, card.DueTimezone, card.DueHasTime, card.DueComplete);
}
public sealed record CardDateChange(CardRecord Card, bool Changed);
public interface ICardDateStore
{
    Task<CardRecord?> SetDatesAsync(Guid organizationId, Guid boardId, Guid cardId, CardDateValues values,
        long version, DateTimeOffset now, CancellationToken cancellationToken);
}

// Date-only due input means the final microsecond of that local calendar day.
// Explicit times are UTC instants; the IANA context is preserved for display.
public static partial class CardDateInput
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$", RegexOptions.CultureInvariant)]
    private static partial Regex UtcPattern();

    public static bool TryNormalize(CardDatesInput input, out CardDateValues? values)
    {
        values = null;
        try
        {
            var hasDates = input.StartAt is not null || input.DueAt is not null;
            if (input.DueAt is null && (input.DueHasTime || input.DueComplete)) return false;
            TimeZoneInfo? zone = null;
            if (input.DueTimezone is not null)
            {
                if (input.DueTimezone.Length is < 1 or > 100 || input.DueTimezone.Trim() != input.DueTimezone ||
                    input.DueTimezone != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(input.DueTimezone, out _)) return false;
                zone = TimeZoneInfo.FindSystemTimeZoneById(input.DueTimezone);
            }
            if (hasDates && zone is null) return false;
            var start = input.StartAt is null ? (DateTimeOffset?)null : Parse(input.StartAt, zone!, false, false);
            var due = input.DueAt is null ? (DateTimeOffset?)null : Parse(input.DueAt, zone!, true, input.DueHasTime);
            if (start > due) return false;
            values = new(start, due, hasDates ? input.DueTimezone : null, input.DueHasTime, input.DueComplete);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException or FormatException)
        { return false; }
    }

    private static DateTimeOffset Parse(string text, TimeZoneInfo zone, bool due, bool timed)
    {
        if (text.Length > 40) throw new FormatException();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            if (due && timed) throw new FormatException();
            // Reject a wholly skipped local date instead of silently changing it.
            var beginning = StartOfDay(day, zone);
            return due ? EndOfDay(day, zone) : beginning;
        }
        if (!UtcPattern().IsMatch(text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) || instant.Offset != TimeSpan.Zero)
            throw new FormatException();
        var normalized = new DateTimeOffset(instant.Ticks - instant.Ticks % 10, TimeSpan.Zero);
        if (due && !timed)
        {
            var dayAtInstant = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(normalized, zone).DateTime);
            if (normalized != EndOfDay(dayAtInstant, zone)) throw new FormatException();
        }
        return normalized;
    }

    private static DateTimeOffset EndOfDay(DateOnly day, TimeZoneInfo zone)
    {
        // A valid day can precede a wholly skipped date (Apia, 2011-12-29).
        // Its boundary is the next existing local day's first instant.
        for (var next = 1; next <= 3; next++)
        {
            try { return StartOfDay(day.AddDays(next), zone).AddTicks(-10); }
            catch (FormatException) { }
        }
        throw new FormatException();
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var minute = 0; minute < 24 * 60; minute++)
        {
            if (!zone.IsInvalidTime(local))
            {
                var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
                var instant = new DateTimeOffset(local, offset).ToUniversalTime();
                if (TimeZoneInfo.ConvertTime(instant, zone).DateTime == local) return instant;
            }
            local = local.AddMinutes(1);
        }
        throw new FormatException();
    }
}
