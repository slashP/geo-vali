using GeoVali.Maps;
using Xunit;

namespace GeoVali.Tests;

public class CadenceTests
{
    // Helper: build a UTC instant that lands on the given local date at local noon,
    // so the test is independent of the machine's time zone.
    private static DateTime UtcForLocalNoonOn(int year, int month, int day) =>
        new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

    [Theory]
    [InlineData(null, 7, 7)]      // no override -> global default
    [InlineData(0, 7, 7)]         // zero is not a valid override -> global default
    [InlineData(-3, 7, 7)]        // negative is not a valid override -> global default
    [InlineData(10, 7, 10)]       // per-map override wins
    [InlineData(1, 30, 1)]        // per-map override wins even when smaller
    public void Resolves_per_map_override_before_global_default(int? overrideDays, int globalDefault, int expected)
    {
        Assert.Equal(expected, Cadence.DaysBetweenUpdates(overrideDays, globalDefault));
    }

    [Fact]
    public void Global_default_is_seven_days()
    {
        Assert.Equal(7, Cadence.DefaultDaysBetweenUpdates);
    }

    [Fact]
    public void Every_one_day_means_not_already_done_today()
    {
        var today = new DateTime(2026, 9, 6, 9, 0, 0, DateTimeKind.Local);

        // Published earlier the same local day -> not due.
        Assert.False(Cadence.IsDue(UtcForLocalNoonOn(2026, 9, 6), 1, today));

        // Published yesterday -> due, no matter what time of day.
        Assert.True(Cadence.IsDue(UtcForLocalNoonOn(2026, 9, 5), 1, today));
    }

    [Fact]
    public void Counts_whole_local_calendar_days_not_elapsed_hours()
    {
        var today = new DateTime(2026, 9, 6, 0, 30, 0, DateTimeKind.Local);

        // 10 local days earlier: exactly due.
        Assert.True(Cadence.IsDue(UtcForLocalNoonOn(2026, 8, 27), 10, today));

        // 9 local days earlier: one day short, even though far more than
        // 9 * 24 hours may have elapsed depending on the clock time.
        Assert.False(Cadence.IsDue(UtcForLocalNoonOn(2026, 8, 28), 10, today));
    }

    [Fact]
    public void A_map_that_has_never_been_published_is_always_due()
    {
        var today = new DateTime(2026, 9, 6, 9, 0, 0, DateTimeKind.Local);
        Assert.True(Cadence.IsDue(DateTime.MinValue, 20, today));
    }

    [Fact]
    public void Treats_an_unspecified_kind_timestamp_as_utc()
    {
        // geoguessr.ephemeral.json written by an older tool may lack the trailing Z.
        var today = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Unspecified);

        // Interpreted as UTC, that is earlier today in any time zone within +/- 10h,
        // so a 1-day cadence is not yet due.
        var result = Cadence.IsDue(unspecified, 1, today);
        var expected = DateOnly.FromDateTime(today) ==
                       DateOnly.FromDateTime(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc).ToLocalTime());
        Assert.Equal(!expected, result);
    }
}
