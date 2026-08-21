using Relay.Domain;

namespace Relay.Tests.Unit;

/// <summary>
/// Requirements §4 "Timezone and week boundaries" — the pure half: DST and ISO-year-boundary
/// arithmetic on <see cref="WeekRange"/>, which is <see cref="DateOnly"/>-only by construction (no
/// <c>TimeZoneInfo</c>, no <c>DateTime.Now</c>). The LA date-shift and <c>TZ=Asia/Tokyo</c>
/// byte-identical assertions are the DB half, in the integration suite.
///
/// Every date fact below was cross-checked against Python's <c>datetime.date.isocalendar()</c> —
/// an independent implementation of ISO 8601 — rather than derived from <see cref="WeekRange"/>
/// itself, so these are not tautological.
/// </summary>
public class WeekRangeTests
{
    [Fact]
    public void FromIsoWeek_MidYearWeek_MatchesKnownDates()
    {
        var week = WeekRange.FromIsoWeek("2026-W30");

        Assert.Equal(new DateOnly(2026, 7, 20), week.Start); // Monday
        Assert.Equal(new DateOnly(2026, 7, 26), week.End);   // Sunday
        Assert.Equal("2026-W30", week.ToIsoWeek());
    }

    [Fact]
    public void FromIsoWeek_WeekLabeledForNextYear_CanStartInDecemberOfThePreviousYear()
    {
        // 2024-12-30 (a Monday) belongs to ISO week 2025-W01, not week 53 of 2024.
        var week = WeekRange.FromIsoWeek("2025-W01");

        Assert.Equal(new DateOnly(2024, 12, 30), week.Start);
        Assert.Equal(new DateOnly(2025, 1, 5), week.End);
        Assert.Equal("2025-W01", week.ToIsoWeek());
    }

    [Fact]
    public void Containing_ThursdayJanuaryFirst_BelongsToAWeekStartingInThePreviousCalendarYear()
    {
        // 2026-01-01 is a Thursday, so its ISO week (2026-W01) started the Monday before,
        // 2025-12-29 — a date in the previous calendar year.
        var week = WeekRange.Containing(new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2025, 12, 29), week.Start);
        Assert.Equal(new DateOnly(2026, 1, 4), week.End);
        Assert.Equal("2026-W01", week.ToIsoWeek());
    }

    [Fact]
    public void FromIsoWeek_53WeekYear_RoundTripsAcrossTheYearBoundary()
    {
        // 2026 has 53 ISO weeks: 2026-W53 runs 2026-12-28 .. 2027-01-03, spanning two calendar
        // years while still round-tripping to the same ISO week label.
        var week = WeekRange.FromIsoWeek("2026-W53");

        Assert.Equal(new DateOnly(2026, 12, 28), week.Start);
        Assert.Equal(new DateOnly(2027, 1, 3), week.End);
        Assert.Equal("2026-W53", week.ToIsoWeek());

        var next = week.Next();
        Assert.Equal(new DateOnly(2027, 1, 4), next.Start);
        Assert.Equal("2027-W01", next.ToIsoWeek());
    }

    [Fact]
    public void Containing_TheDstTransitionSunday_StaysASevenDayWeek()
    {
        // 2026-03-08 is the US "spring forward" DST transition date and also an ISO week
        // boundary (the Sunday ending 2026-W10). WeekRange is DateOnly-only, so DST cannot change
        // its day count by construction — this pins that down as a regression guard, not an
        // assumption.
        var week = WeekRange.Containing(new DateOnly(2026, 3, 8));

        Assert.Equal(new DateOnly(2026, 3, 2), week.Start);
        Assert.Equal(new DateOnly(2026, 3, 8), week.End);
        Assert.Equal(6, week.End.DayNumber - week.Start.DayNumber); // exactly 7 days, never 6 or 8

        var next = week.Next();
        Assert.Equal(new DateOnly(2026, 3, 9), next.Start); // no gap, no overlap across the transition
        Assert.Equal(new DateOnly(2026, 3, 15), next.End);
        Assert.Equal(6, next.End.DayNumber - next.Start.DayNumber);
    }

    [Fact]
    public void Preceding_ReturnsWeeksOldestFirst_AndNeverIncludesTheViewedWeek()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");

        var preceding = viewedWeek.Preceding(3).ToList();

        Assert.Equal(3, preceding.Count);
        Assert.Equal(new DateOnly(2026, 6, 29), preceding[0].Start); // -3
        Assert.Equal(new DateOnly(2026, 7, 6), preceding[1].Start);  // -2
        Assert.Equal(new DateOnly(2026, 7, 13), preceding[2].Start); // -1
        Assert.DoesNotContain(preceding, w => w.Start == viewedWeek.Start);
    }

    [Fact]
    public void FromIsoWeek_RejectsMalformedInput()
    {
        Assert.Throws<FormatException>(() => WeekRange.FromIsoWeek("2026-30"));
        Assert.Throws<FormatException>(() => WeekRange.FromIsoWeek("not-a-week"));
        Assert.Throws<FormatException>(() => WeekRange.FromIsoWeek("2026-W99"));
    }
}
