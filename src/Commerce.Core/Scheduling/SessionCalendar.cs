using Commerce.Core.Catalog;

namespace Commerce.Core.Scheduling;

/// <summary>One concrete class, in UTC. Render it in a zone with <see cref="TimeFormat"/>.</summary>
public sealed record ScheduledSession(DateTimeOffset Start, DateTimeOffset End);

/// <summary>Expands a <see cref="RecurringSchedule"/> into concrete, DST-correct sessions.</summary>
public static class SessionCalendar
{
    // A year of weekly sessions is plenty for any question an assistant will ask.
    private const int HorizonDays = 400;

    public static IEnumerable<ScheduledSession> StartingFrom(RecurringSchedule schedule, DateTimeOffset fromInclusive)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var days = schedule.Days.ToHashSet();
        var cancelled = schedule.CancelledDates.ToHashSet();
        var duration = TimeSpan.FromMinutes(schedule.DurationMinutes);

        // Start a day early: "from" may already be the next day in the anchor zone, or not yet.
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromInclusive, zone).DateTime).AddDays(-1);
        for (var i = 0; i < HorizonDays; i++, date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek) || cancelled.Contains(date))
                continue;

            var start = ToUtc(date.ToDateTime(schedule.StartTime), zone);
            if (start >= fromInclusive)
                yield return new ScheduledSession(start, start + duration);
        }
    }

    private static DateTimeOffset ToUtc(DateTime wallClock, TimeZoneInfo zone)
    {
        // A wall-clock time inside a spring-forward gap doesn't exist; organisers start an hour later.
        if (zone.IsInvalidTime(wallClock))
            wallClock = wallClock.AddHours(1);
        return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock)).ToUniversalTime();
    }
}
