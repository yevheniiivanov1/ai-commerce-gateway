using System.Text.RegularExpressions;
using System.Globalization;

namespace Commerce.Core.Scheduling;

public static partial class TimeFormat
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Accepts IANA ids plus the abbreviations people (and assistants) actually type.</summary>
    public static bool TryResolveZone(string? value, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var id = value.Trim().ToUpperInvariant() switch
        {
            "UTC" or "GMT" or "Z" => "Etc/UTC",
            "PT" or "PST" or "PDT" or "PACIFIC" => "America/Los_Angeles",
            "MT" or "MST" or "MDT" or "MOUNTAIN" => "America/Denver",
            "CT" or "CST" or "CDT" or "CENTRAL" => "America/Chicago",
            "ET" or "EST" or "EDT" or "EASTERN" => "America/New_York",
            "UK" or "BST" => "Europe/London",
            "CET" or "CEST" => "Europe/Berlin",
            "EET" or "EEST" => "Europe/Kyiv",
            "ICT" => "Asia/Bangkok",
            "JST" => "Asia/Tokyo",
            "AEST" or "AEDT" => "Australia/Sydney",
            _ => value.Trim(),
        };
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found))
        {
            zone = found;
            return true;
        }
        return false;
    }

    /// <summary>"Sat Nov 7, 2026 · 10:00–10:45 (America/New_York, UTC−05:00)".</summary>
    public static string Describe(ScheduledSession session, TimeZoneInfo zone)
    {
        var start = TimeZoneInfo.ConvertTime(session.Start, zone);
        var end = TimeZoneInfo.ConvertTime(session.End, zone);
        return $"{start.ToString("ddd MMM d, yyyy", En)} · {start:HH:mm}–{end:HH:mm} ({ZoneLabel(zone, start)})";
    }

    public static string DescribeInstant(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return $"{local.ToString("ddd MMM d, yyyy", En)} {local:HH:mm} ({ZoneLabel(zone, local)})";
    }

    public static string ZoneLabel(TimeZoneInfo zone, DateTimeOffset at)
    {
        var offset = at.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{IanaId(zone)}, UTC{sign}{offset.Duration():hh\\:mm}";
    }

    public static string IanaId(TimeZoneInfo zone) =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;

    public static string DayName(DayOfWeek day) => En.DateTimeFormat.GetDayName(day);

    /// <summary>"Saturday and Sunday", "Monday, Wednesday and Friday".</summary>
    public static string Days(IReadOnlyList<DayOfWeek> days)
    {
        var names = days.Select(DayName).ToList();
        return names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }

    /// <summary>"every weekend (Saturday and Sunday)", "every Tuesday and Thursday".</summary>
    public static string Recurrence(IReadOnlyList<DayOfWeek> days, string? qualifier = null)
    {
        var names = Days(days);
        if (days.Count == 7)
            return "every day" + (qualifier is null ? "" : $" ({qualifier})");
        if (days.Count == 2 && days.Contains(DayOfWeek.Saturday) && days.Contains(DayOfWeek.Sunday))
            return qualifier is null ? $"every weekend ({names})" : $"every weekend ({names}, {qualifier})";
        return qualifier is null ? $"every {names}" : $"every {names} ({qualifier})";
    }

    public static string Date(DateOnly date) => date.ToString("ddd MMM d, yyyy", En);

    public static string LongDate(DateOnly date) => date.ToString("MMMM d, yyyy", En);

    /// <summary>
    /// Parses what an assistant passes for "November 6": ISO dates, or month/day with or without
    /// a year. A date without a year means its next occurrence on or after <paramref name="today"/>.
    /// </summary>
    public static bool TryParseDate(string? value, DateOnly today, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var text = Ordinal().Replace(IsoTime().Replace(value.Trim(), ""), "$1");

        string[] withYear = ["yyyy-MM-dd", "yyyy-M-d", "MMMM d, yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMM d yyyy", "d MMMM yyyy", "d MMM yyyy", "M/d/yyyy"];
        if (DateOnly.TryParseExact(text, withYear, En, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        string[] withoutYear = ["MMMM d", "MMM d", "d MMMM", "d MMM", "M/d", "MM-dd"];
        foreach (var format in withoutYear)
        {
            if (DateTime.TryParseExact(text, format, En, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                date = new DateOnly(today.Year, parsed.Month, parsed.Day);
                if (date < today)
                    date = date.AddYears(1);
                return true;
            }
        }
        return false;
    }

    // "2026-11-06T00:00:00Z" → "2026-11-06": only the calendar date matters.
    [GeneratedRegex(@"(?<=^\d{4}-\d{2}-\d{2})T.*$")]
    private static partial Regex IsoTime();

    // "Nov 6th", "November 1st, 2027" → "Nov 6", "November 1, 2027".
    [GeneratedRegex(@"(\d)(st|nd|rd|th)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ordinal();
}
