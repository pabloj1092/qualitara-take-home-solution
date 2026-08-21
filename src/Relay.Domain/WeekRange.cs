using System.Globalization;

namespace Relay.Domain;

/// <summary>
/// A Monday-to-Sunday ISO week, expressed entirely in <see cref="DateOnly"/>. No <see
/// cref="TimeZoneInfo"/>, no <see cref="DateTime.Now"/>: timezone conversion happens once, in
/// SQL, before a date ever reaches this type.
/// </summary>
public readonly record struct WeekRange(DateOnly Start, DateOnly End)
{
    public static WeekRange FromIsoWeek(string isoWeek)
    {
        if (!TryParseIsoWeek(isoWeek, out var year, out var week))
        {
            throw new FormatException($"'{isoWeek}' is not a valid ISO week (expected yyyy-Www).");
        }

        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
        return new WeekRange(monday, monday.AddDays(6));
    }

    public static WeekRange Containing(DateOnly date)
    {
        // Monday = 0 .. Sunday = 6, independent of ISOWeek's year-boundary handling — the week
        // containing a date is always the calendar week its Monday belongs to.
        var offsetFromMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-offsetFromMonday);
        return new WeekRange(monday, monday.AddDays(6));
    }

    public string ToIsoWeek()
    {
        var monday = Start.ToDateTime(TimeOnly.MinValue);
        var year = ISOWeek.GetYear(monday);
        var week = ISOWeek.GetWeekOfYear(monday);
        return $"{year:D4}-W{week:D2}";
    }

    public WeekRange Previous() => new(Start.AddDays(-7), End.AddDays(-7));

    public WeekRange Next() => new(Start.AddDays(7), End.AddDays(7));

    /// <summary>The <paramref name="n"/> weeks immediately before this one, oldest first (-n .. -1).
    /// The viewed week itself is never included.</summary>
    public IEnumerable<WeekRange> Preceding(int n)
    {
        for (var i = n; i >= 1; i--)
        {
            yield return new WeekRange(Start.AddDays(-7 * i), End.AddDays(-7 * i));
        }
    }

    public string Label() =>
        $"Week of {Start.ToString("ddd d MMM", CultureInfo.InvariantCulture)} " +
        $"– {End.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture)}";

    private static bool TryParseIsoWeek(string? s, out int year, out int week)
    {
        year = 0;
        week = 0;
        if (s is not { Length: 8 } || s[4] != '-' || s[5] != 'W')
        {
            return false;
        }

        return int.TryParse(s.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(s.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out week)
            && week is >= 1 and <= 53;
    }
}
