using Commerce.Core.Catalog;

namespace Commerce.Core.Scheduling;

public sealed record Availability
{
    public required bool OpenForEnrollment { get; init; }
    public required EnrollmentMode Mode { get; init; }
    public DateOnly? RequestedDate { get; init; }
    public bool RequestedDateHasClass { get; init; }
    public bool RequestedDateHasPassed { get; init; }
    /// <summary>The next classes on or after the requested date (or now), in UTC.</summary>
    public required IReadOnlyList<ScheduledSession> UpcomingSessions { get; init; }
    /// <summary>The first class a buyer who enrolls right now is guaranteed to have links for.</summary>
    public ScheduledSession? EarliestFirstClass { get; init; }
    /// <summary>The first class matching the request: the requested date if possible, else the nearest one after it.</summary>
    public ScheduledSession? SuggestedFirstClass { get; init; }
    /// <summary>Latest moment to pay and still receive links before <see cref="SuggestedFirstClass"/>.</summary>
    public DateTimeOffset? EnrollBy { get; init; }
    public DateOnly? TermEndsAround { get; init; }
    public required TimeZoneInfo ViewerZone { get; init; }
    public required string Explanation { get; init; }
}

/// <summary>
/// Answers "can I join around date X" from the recurring rule, not from a date printed on a
/// landing page. All wording is generated here so every channel says the same thing.
/// </summary>
public sealed class AvailabilityService(TimeProvider clock)
{
    private const int SessionsToShow = 4;

    public Availability Check(Product product, DateOnly? requestedDate, TimeZoneInfo? viewerZone)
    {
        var schedule = product.Schedule;
        var anchorZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var zone = viewerZone ?? anchorZone;
        var now = clock.GetUtcNow();
        var today = LocalDate(now, zone);
        var leadTime = TimeSpan.FromHours(schedule.Enrollment.AccessLeadTimeHours);

        var open = product.Status == ProductStatus.Active
            && schedule.Enrollment.Mode != EnrollmentMode.Closed
            && product.Offers.Any(o => o.Availability == OfferAvailability.Open);

        var hasPassed = requestedDate < today;
        var dayStart = requestedDate is { } d && !hasPassed ? StartOfDay(d, zone) : now;

        // Whether the requested day has a class follows from the rule alone, not from whether that
        // class is still ahead of us: "is there a class today?" at 6 pm is still "yes, at 7 am".
        var classThatDay = requestedDate is { } day && !hasPassed
            ? SessionCalendar.StartingFrom(schedule, dayStart).TakeWhile(s => LocalDate(s.Start, zone) <= day).FirstOrDefault()
            : null;

        var upcoming = SessionCalendar.StartingFrom(schedule, Max(now, dayStart)).Take(SessionsToShow).ToList();
        var earliest = SessionCalendar.StartingFrom(schedule, now + leadTime).FirstOrDefault();
        var first = open ? SessionCalendar.StartingFrom(schedule, Max(now + leadTime, dayStart)).FirstOrDefault() : null;

        var availability = new Availability
        {
            OpenForEnrollment = open,
            Mode = schedule.Enrollment.Mode,
            RequestedDate = requestedDate,
            RequestedDateHasClass = classThatDay is not null,
            RequestedDateHasPassed = hasPassed,
            UpcomingSessions = upcoming,
            EarliestFirstClass = earliest,
            SuggestedFirstClass = first,
            EnrollBy = first is not null ? first.Start - leadTime : null,
            TermEndsAround = first is not null ? LocalDate(first.Start, zone).AddMonths(product.PrimaryOffer.Term.Months) : null,
            ViewerZone = zone,
            Explanation = "",
        };
        return availability with { Explanation = Explain(product, availability, classThatDay, now, zone) };
    }

    private static string Explain(Product product, Availability a, ScheduledSession? classThatDay, DateTimeOffset now, TimeZoneInfo zone)
    {
        var schedule = product.Schedule;
        var leadHours = schedule.Enrollment.AccessLeadTimeHours;
        var lines = new List<string>();

        if (!a.OpenForEnrollment)
            return $"{product.Name} is not open for enrollment right now. Contact the merchant for the next opening.";

        lines.Add(schedule.Enrollment.Mode == EnrollmentMode.Rolling
            ? $"Enrollment is open: {product.ShortName} runs {Recurrence(schedule, zone, now)}, year-round, and you can join on any day."
            : $"Enrollment is open for {product.ShortName}.");

        var nearest = string.Join(" and ", a.UpcomingSessions.Take(2).Select(s => TimeFormat.Describe(s, zone)));
        switch (a.RequestedDate)
        {
            case null:
                lines.Add($"The next classes are {nearest}.");
                break;
            case { } requested when a.RequestedDateHasPassed:
                lines.Add($"{TimeFormat.LongDate(requested)} has already passed. The next classes are {nearest}.");
                break;
            case { } requested when classThatDay is null:
                lines.Add($"{TimeFormat.LongDate(requested)} is a {TimeFormat.DayName(requested.DayOfWeek)}, so there is no class that day. The nearest classes are {nearest}.");
                break;
            case { } requested:
                var theClass = $"The class on {TimeFormat.LongDate(requested)} ({TimeFormat.Describe(classThatDay, zone)})";
                lines.Add(classThatDay.End <= now ? $"{theClass} has already taken place."
                    : classThatDay.Start <= now ? $"{theClass} is already under way."
                    : classThatDay.Start - now < TimeSpan.FromHours(leadHours) ? $"{theClass} starts in less than {leadHours} hours, too soon for a new member to receive the session links."
                    : $"There is a class on {TimeFormat.LongDate(requested)}: {TimeFormat.Describe(classThatDay, zone)}.");
                break;
        }

        if (a.SuggestedFirstClass is { } first && a.EnrollBy is { } enrollBy)
            lines.Add($"To start with the class on {TimeFormat.Describe(first, zone)}, enroll by {TimeFormat.DescribeInstant(enrollBy, zone)}; session links arrive within {leadHours} hours of payment.");

        if (a.TermEndsAround is { } end)
        {
            var term = product.PrimaryOffer.Term;
            lines.Add($"A {term.Months}-month term from that start runs until about {TimeFormat.Date(end)} ({term.Classes} classes).");
        }

        if (product.LandingPage.DisplayedStartDate is { } banner)
            lines.Add($"The \"{banner}\" banner on the landing page only names an upcoming session at the time the page was edited; it is not a deadline.");

        return string.Join(" ", lines);
    }

    /// <summary>
    /// "every weekend (Saturday and Sunday)", plus the viewer's own weekdays when the organiser's
    /// days land on different ones for them (a Saturday 7 am Pacific class is a Sunday in Auckland).
    /// </summary>
    public static string Recurrence(RecurringSchedule schedule, TimeZoneInfo viewer, DateTimeOffset now)
    {
        var anchor = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var localDay = SessionCalendar.StartingFrom(schedule, now)
            .Take(schedule.Days.Count)
            .ToDictionary(s => TimeZoneInfo.ConvertTime(s.Start, anchor).DayOfWeek, s => TimeZoneInfo.ConvertTime(s.Start, viewer).DayOfWeek);
        var viewerDays = schedule.Days.Select(d => localDay.GetValueOrDefault(d, d)).ToList();

        var qualifier = viewerDays.SequenceEqual(schedule.Days)
            ? null
            : $"{TimeFormat.IanaId(anchor)} time; {TimeFormat.Days(viewerDays)} in {TimeFormat.IanaId(viewer)}";
        return TimeFormat.Recurrence(schedule.Days, qualifier);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
