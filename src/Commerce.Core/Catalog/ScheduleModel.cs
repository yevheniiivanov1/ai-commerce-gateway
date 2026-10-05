namespace Commerce.Core.Catalog;

public enum EnrollmentMode
{
    /// <summary>Join any time; you start with the next session after access is granted.</summary>
    Rolling,
    /// <summary>Fixed start dates; enrollment is only meaningful before a cohort starts.</summary>
    Cohort,
    Closed,
}

public sealed record EnrollmentPolicy
{
    public required EnrollmentMode Mode { get; init; }
    /// <summary>How long after payment the buyer gets access (session links).</summary>
    public int AccessLeadTimeHours { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// The business rule behind the timetable: weekly sessions at a wall-clock time in the
/// organiser's time zone. Concrete dates are computed from it, never stored.
/// </summary>
public sealed record RecurringSchedule
{
    public required IReadOnlyList<DayOfWeek> Days { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required int DurationMinutes { get; init; }
    /// <summary>IANA id of the zone the start time is anchored to.</summary>
    public required string TimeZone { get; init; }
    public required EnrollmentPolicy Enrollment { get; init; }
    /// <summary>Dates (in the anchor zone) on which no session takes place.</summary>
    public IReadOnlyList<DateOnly> CancelledDates { get; init; } = [];

    public int SessionsPerWeek => Days.Count;
}
