using System.Globalization;

namespace Commerce.Core.Scheduling;

public static class TimeFormat
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
        var text = value.Trim();

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
}
