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
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var offer = product.Offers[0];

        var open = product.Status == ProductStatus.Active
            && schedule.Enrollment.Mode != EnrollmentMode.Closed
            && product.Offers.Any(o => o.Availability == OfferAvailability.Open);

        var hasPassed = requestedDate < today;
        var from = requestedDate is { } d && !hasPassed ? StartOfDay(d, zone) : now;
        if (from < now)
            from = now;

        var upcoming = SessionCalendar.StartingFrom(schedule, from).Take(SessionsToShow).ToList();
        var leadTime = TimeSpan.FromHours(schedule.Enrollment.AccessLeadTimeHours);
        var earliest = SessionCalendar.StartingFrom(schedule, now + leadTime).FirstOrDefault();

        var onRequestedDay = requestedDate is { } day && !hasPassed
            ? upcoming.FirstOrDefault(s => LocalDate(s.Start, zone) == day)
            : null;
        var suggested = new[] { upcoming.FirstOrDefault(), earliest }
            .Where(s => s is not null)
            .MaxBy(s => s!.Start);

        var availability = new Availability
        {
            OpenForEnrollment = open,
            Mode = schedule.Enrollment.Mode,
            RequestedDate = requestedDate,
            RequestedDateHasClass = onRequestedDay is not null,
            RequestedDateHasPassed = hasPassed,
            UpcomingSessions = upcoming,
            EarliestFirstClass = earliest,
            SuggestedFirstClass = open ? suggested : null,
            EnrollBy = open && suggested is not null ? suggested.Start - leadTime : null,
            TermEndsAround = open && suggested is not null
                ? LocalDate(suggested.Start, zone).AddMonths(offer.Term.Months)
                : null,
            ViewerZone = zone,
            Explanation = "",
        };
        return availability with { Explanation = Explain(product, availability, onRequestedDay, zone) };
    }

    private static string Explain(Product product, Availability a, ScheduledSession? onRequestedDay, TimeZoneInfo zone)
    {
        var schedule = product.Schedule;
        var days = string.Join(" and ", schedule.Days.Select(TimeFormat.DayName));
        var lines = new List<string>();

        if (!a.OpenForEnrollment)
        {
            lines.Add($"{product.Name} is not open for enrollment right now. Contact the merchant for the next opening.");
            return string.Join(" ", lines);
        }

        lines.Add(schedule.Enrollment.Mode == EnrollmentMode.Rolling
            ? $"Enrollment is open: {product.ShortName} runs every {days}, year-round, and you can join on any day."
            : $"Enrollment is open for {product.ShortName}.");

        if (a.RequestedDate is { } requested)
        {
            var requestedText = TimeFormat.LongDate(requested);
            if (a.RequestedDateHasPassed)
                lines.Add($"{requestedText} has already passed; the next classes are listed below.");
            else if (onRequestedDay is not null)
                lines.Add($"There is a class on {requestedText}: {TimeFormat.Describe(onRequestedDay, zone)}.");
            else
            {
                var nearest = string.Join(" and ", a.UpcomingSessions.Take(2).Select(s => TimeFormat.Describe(s, zone)));
                lines.Add($"{requestedText} is a {TimeFormat.DayName(requested.DayOfWeek)}, so there is no class that day. The nearest classes are {nearest}.");
            }

            if (onRequestedDay is not null && a.SuggestedFirstClass is { } first && first.Start > onRequestedDay.Start)
                lines.Add($"That class starts less than {schedule.Enrollment.AccessLeadTimeHours} hours from now, so the first class a new member is guaranteed to get links for is {TimeFormat.Describe(first, zone)}.");
        }

        if (a.SuggestedFirstClass is not null && a.EnrollBy is { } enrollBy)
            lines.Add($"Enroll by {TimeFormat.DescribeInstant(enrollBy, zone)} to start then — session links arrive within {schedule.Enrollment.AccessLeadTimeHours} hours of payment.");

        if (a.TermEndsAround is { } end)
        {
            var offer = product.Offers[0];
            lines.Add($"A {offer.Term.Months}-month term from that start runs until about {TimeFormat.Date(end)} ({offer.Term.Classes} classes).");
        }

        if (product.LandingPage.DisplayedStartDate is { } banner)
            lines.Add($"The \"{banner}\" banner on the landing page only names the next weekend at the time the page was edited; it is not a deadline.");

        return string.Join(" ", lines);
    }

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
