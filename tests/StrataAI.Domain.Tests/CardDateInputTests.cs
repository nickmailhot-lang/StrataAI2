using System.Globalization;
using StrataAI.Application.WorkManagement;
using Xunit;

namespace StrataAI.Domain.Tests;

public sealed class CardDateInputTests
{
    [Theory]
    [InlineData("2026-03-08", "2026-03-08T08:00:00Z", "2026-03-09T06:59:59.999999Z", 23)]
    [InlineData("2025-11-02", "2025-11-02T07:00:00Z", "2025-11-03T07:59:59.999999Z", 25)]
    [InlineData("2026-11-01", "2026-11-01T07:00:00Z", "2026-11-02T06:59:59.999999Z", 24)]
    [InlineData("2026-10-02", "2026-10-02T07:00:00Z", "2026-10-03T06:59:59.999999Z", 24)]
    public void Date_only_uses_the_entire_local_calendar_day_across_DST_and_roundtrips(string day, string start, string due, int hours)
    {
        Assert.True(CardDateInput.TryNormalize(new(day, day, "America/Vancouver", false, false, 1), out var values));
        Assert.NotNull(values);
        Assert.Equal(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture), values.StartAt);
        Assert.Equal(DateTimeOffset.Parse(due, CultureInfo.InvariantCulture), values.DueAt);
        Assert.Equal(TimeSpan.FromHours(hours), values.DueAt!.Value.AddTicks(10) - values.StartAt!.Value);
        Assert.True(CardDateInput.TryNormalize(new(start, due, "America/Vancouver", false, true, 2), out var completed));
        Assert.Equal(values with { DueComplete = true }, completed);
    }

    [Fact]
    public void Valid_day_before_a_wholly_skipped_date_ends_at_the_next_existing_local_day()
    {
        Assert.True(CardDateInput.TryNormalize(new("2011-12-29", "2011-12-29", "Pacific/Apia", false, false, 1), out var values));
        Assert.NotNull(values);
        Assert.Equal(DateTimeOffset.Parse("2011-12-30T09:59:59.999999Z", CultureInfo.InvariantCulture), values.DueAt);
        Assert.True(CardDateInput.TryNormalize(new(null, values.DueAt!.Value.ToString("O"), "Pacific/Apia", false, true, 2), out _));
    }

    [Fact]
    public void Explicit_UTC_times_preserve_context_and_database_microsecond_precision()
    {
        Assert.True(CardDateInput.TryNormalize(new(null, "2026-10-02T12:34:56.1234567+00:00", "Asia/Kolkata", true, true, 1), out var values));
        Assert.NotNull(values); Assert.Equal("Asia/Kolkata", values.DueTimezone);
        Assert.Equal(DateTimeOffset.Parse("2026-10-02T12:34:56.123456Z", CultureInfo.InvariantCulture), values.DueAt);
        Assert.True(values.DueComplete); Assert.True(values.DueHasTime);
    }

    [Theory]
    [InlineData(null, "2026-10-02", null, false, false)]
    [InlineData(null, "2026-10-02", "Pacific Standard Time", false, false)]
    [InlineData(null, "2026-10-02", "Unknown/Place", false, false)]
    [InlineData(null, "2026-10-02", " UTC", false, false)]
    [InlineData(null, "2026-02-30", "UTC", false, false)]
    [InlineData(null, "2026-10-02", "UTC", true, false)]
    [InlineData(null, "2026-10-02T12:00:00Z", "UTC", false, false)]
    [InlineData(null, "2026-10-02T12:00:00", "UTC", true, false)]
    [InlineData(null, "2026-10-02T12:00:00+01:00", "UTC", true, false)]
    [InlineData("2026-10-03", "2026-10-02", "UTC", false, false)]
    [InlineData(null, null, null, true, false)]
    [InlineData(null, null, null, false, true)]
    [InlineData(null, "2011-12-30", "Pacific/Apia", false, false)]
    public void Invalid_context_order_flags_and_wholly_skipped_days_are_rejected(string? start, string? due, string? zone, bool timed, bool complete)
    {
        Assert.False(CardDateInput.TryNormalize(new(start, due, zone, timed, complete, 1), out var values)); Assert.Null(values);
    }

    [Fact]
    public void Clearing_both_dates_clears_context_and_start_only_is_supported()
    {
        Assert.True(CardDateInput.TryNormalize(new(null, null, "UTC", false, false, 1), out var clear));
        Assert.Equal(new(null, null, null, false, false), clear);
        Assert.True(CardDateInput.TryNormalize(new("2026-10-02", null, "UTC", false, false, 1), out var start));
        Assert.NotNull(start); Assert.NotNull(start.StartAt); Assert.Null(start.DueAt);
    }
}
