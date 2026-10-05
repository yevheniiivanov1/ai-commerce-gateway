using Commerce.Core.Catalog;
using Commerce.Core.Scheduling;

namespace Commerce.Tests;

public class AvailabilityTests
{
    private readonly Catalog _catalog = TestCatalog.Load();
    private Product DoubleAxel => _catalog.Find("vsa-double-axel-club")!;

    [Fact]
    public void November_6_is_a_Friday_so_the_answer_is_the_weekend_around_it_not_a_refusal()
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel, new DateOnly(2026, 11, 6), TestCatalog.Zone("America/New_York"));

        Assert.True(a.OpenForEnrollment);
        Assert.False(a.RequestedDateHasClass);
        // US clocks fall back on Nov 1: 07:00 Pacific is 15:00 UTC from then on.
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero), a.UpcomingSessions[0].Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero), a.UpcomingSessions[1].Start);
        Assert.Equal(a.UpcomingSessions[0], a.SuggestedFirstClass);
        Assert.Contains("November 6, 2026 is a Friday", a.Explanation);
        Assert.Contains("Sat Nov 7, 2026 · 10:00–10:45 (America/New_York, UTC-05:00)", a.Explanation);
        Assert.Contains("not a deadline", a.Explanation);
    }

    [Theory]
    [InlineData("2026-10-03")] // before the landing page's "October 10" — already past
    [InlineData("2026-12-19")]
    [InlineData("2027-03-20")]
    [InlineData("2027-09-25")]
    public void The_landing_page_start_date_never_limits_enrollment(string date)
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel, DateOnly.Parse(date), null);

        Assert.True(a.OpenForEnrollment);
        Assert.NotNull(a.SuggestedFirstClass);
        Assert.NotEmpty(a.UpcomingSessions);
    }

    [Fact]
    public void A_past_date_is_answered_with_the_next_classes()
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel, new DateOnly(2026, 10, 3), null);

        Assert.True(a.RequestedDateHasPassed);
        Assert.All(a.UpcomingSessions, s => Assert.True(s.Start > TestCatalog.BaselineEvening));
        Assert.Contains("has already passed", a.Explanation);
    }

    [Fact]
    public void A_class_less_than_the_access_lead_time_away_is_not_promised()
    {
        // Friday 20:00 UTC: Saturday's 14:00 UTC class is 18 hours away, links take up to 24.
        var service = new AvailabilityService(TestCatalog.Clock(new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero)));

        var a = service.Check(DoubleAxel, new DateOnly(2026, 10, 10), TestCatalog.Zone("America/Los_Angeles"));

        Assert.True(a.RequestedDateHasClass);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 14, 0, 0, TimeSpan.Zero), a.SuggestedFirstClass!.Start);
        Assert.Contains("less than 24 hours", a.Explanation);
    }

    [Fact]
    public void Asking_about_today_after_its_class_says_the_class_took_place_not_that_there_is_none()
    {
        // Saturday 18:00 UTC: the 14:00 UTC class is over.
        var service = new AvailabilityService(TestCatalog.Clock(new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.Zero)));

        var a = service.Check(DoubleAxel, new DateOnly(2026, 10, 10), TestCatalog.Zone("America/Los_Angeles"));

        Assert.True(a.RequestedDateHasClass);
        Assert.DoesNotContain("no class that day", a.Explanation);
        Assert.Contains("The class on October 10, 2026 (Sat Oct 10, 2026 · 07:00–07:45", a.Explanation);
        Assert.Contains("has already taken place", a.Explanation);
        // Sunday's class is under 24 hours away, so the first one a new member gets links for is next Saturday.
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 14, 0, 0, TimeSpan.Zero), a.SuggestedFirstClass!.Start);
        Assert.Contains("To start with the class on Sat Oct 17, 2026 · 07:00–07:45 (America/Los_Angeles, UTC-07:00), enroll by Fri Oct 16, 2026 07:00", a.Explanation);
    }

    [Fact]
    public void Without_a_date_the_answer_names_the_next_classes_and_the_first_one_to_aim_for()
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel, null, null);

        Assert.Contains("The next classes are Sat Oct 10, 2026 · 07:00–07:45 (America/Los_Angeles, UTC-07:00) and Sun Oct 11, 2026", a.Explanation);
        Assert.Contains("To start with the class on Sat Oct 10, 2026 · 07:00–07:45 (America/Los_Angeles, UTC-07:00), enroll by Fri Oct 9, 2026 07:00", a.Explanation);
        Assert.DoesNotContain("listed below", a.Explanation);
        Assert.DoesNotContain("to start then", a.Explanation);
    }

    [Fact]
    public void A_past_date_names_the_next_classes_in_the_answer_itself()
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel, new DateOnly(2026, 9, 26), null);

        Assert.Contains("September 26, 2026 has already passed. The next classes are Sat Oct 10, 2026", a.Explanation);
    }

    [Fact]
    public void Viewers_across_the_date_line_are_told_which_local_days_the_classes_fall_on()
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel, null, TestCatalog.Zone("Pacific/Auckland"));

        Assert.Contains("every weekend (Saturday and Sunday, America/Los_Angeles time; Sunday and Monday in Pacific/Auckland)", a.Explanation);
    }

    [Fact]
    public void Class_times_follow_daylight_saving_unlike_the_static_table_on_the_landing_page()
    {
        // The page says "9 - 9.45 PM (ICT)". True in summer only: Bangkok has no DST, California does.
        var bangkok = TestCatalog.Zone("Asia/Bangkok");

        var october = SessionCalendar.StartingFrom(DoubleAxel.Schedule, TestCatalog.BaselineEvening).First();
        var november = SessionCalendar.StartingFrom(DoubleAxel.Schedule, new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)).First();

        Assert.Equal(21, TimeZoneInfo.ConvertTime(october.Start, bangkok).Hour);
        Assert.Equal(22, TimeZoneInfo.ConvertTime(november.Start, bangkok).Hour);
    }

    [Fact]
    public void Sessions_land_on_the_configured_weekdays_and_skip_cancelled_dates()
    {
        var schedule = DoubleAxel.Schedule with { CancelledDates = [new DateOnly(2026, 10, 11)] };
        var la = TestCatalog.Zone("America/Los_Angeles");

        var sessions = SessionCalendar.StartingFrom(schedule, TestCatalog.BaselineEvening).Take(4)
            .Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.Start, la).DateTime))
            .ToList();

        Assert.Equal([new(2026, 10, 10), new(2026, 10, 17), new(2026, 10, 18), new(2026, 10, 24)], sessions);
    }

    [Fact]
    public void A_paused_program_is_reported_closed()
    {
        var service = new AvailabilityService(TestCatalog.Clock());

        var a = service.Check(DoubleAxel with { Status = ProductStatus.Paused }, null, null);

        Assert.False(a.OpenForEnrollment);
        Assert.Null(a.SuggestedFirstClass);
        Assert.Contains("not open for enrollment", a.Explanation);
    }

    [Theory]
    [InlineData("2026-11-06", 2026, 11, 6)]
    [InlineData("November 6", 2026, 11, 6)]
    [InlineData("Nov 6, 2027", 2027, 11, 6)]
    [InlineData("March 1", 2027, 3, 1)] // no year and already past this year → next March
    [InlineData("2026-11-06T00:00:00Z", 2026, 11, 6)]
    [InlineData("Nov 6th", 2026, 11, 6)]
    [InlineData("November 1st, 2027", 2027, 11, 1)]
    public void Dates_are_read_the_way_assistants_pass_them(string text, int year, int month, int day)
    {
        Assert.True(TimeFormat.TryParseDate(text, new DateOnly(2026, 10, 5), out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("EST", "America/New_York")]
    [InlineData("Europe/London", "Europe/London")]
    [InlineData("CET", "Europe/Berlin")]
    public void Time_zones_accept_iana_ids_and_common_abbreviations(string input, string iana)
    {
        Assert.True(TimeFormat.TryResolveZone(input, out var zone));
        Assert.Equal(iana, TimeFormat.IanaId(zone));
    }
}
