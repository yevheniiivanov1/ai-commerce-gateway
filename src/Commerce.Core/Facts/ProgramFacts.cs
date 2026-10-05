using Commerce.Core.Catalog;
using Commerce.Core.Scheduling;
using Commerce.Core.Search;

namespace Commerce.Core.Facts;

/// <summary>Projects catalog entries into the AI-facing views.</summary>
public sealed class ProgramFacts(Catalog.Catalog catalog, AvailabilityService availability, TimeProvider clock)
{
    public Merchant Merchant => catalog.Merchant;

    public string BrandName => catalog.Merchant.BrandNames.Where(b => b != catalog.Merchant.Name).ToList() is { Count: > 0 } others
        ? $"{catalog.Merchant.Name} ({string.Join(" / ", others)})"
        : catalog.Merchant.Name;

    public ProgramCard Card(ProductMatch match, Uri publicBaseUrl) => Card(match.Product, publicBaseUrl) with
    {
        WithinBudget = match.WithinBudget,
        WhyItMatches = match.Reasons,
    };

    public ProgramCard Card(Product product, Uri publicBaseUrl)
    {
        var offer = product.PrimaryOffer;
        return new ProgramCard
        {
            ProgramId = product.Id,
            Name = product.Name,
            Brand = BrandName,
            Skill = product.Skill,
            Level = $"{product.Level.Label} — {product.Level.Audience}",
            Format = FormatLine(product),
            Price = offer.BillingSummary,
            PriceAmount = offer.Price.Amount,
            Currency = offer.Price.Currency,
            Duration = $"{offer.Term.Months} months · {offer.Term.Classes} live classes",
            Schedule = Pattern(product.Schedule),
            Instructors = string.Join(", ", product.Instructors.Select(i => i.Name)),
            Summary = product.Summary,
            Enrollment = EnrollmentLine(product),
            FreeTrial = product.FreeTrial is { } trial ? $"{trial.Name}: {trial.Url}" : null,
            OfferId = offer.Id,
            FactSheet = new Uri(publicBaseUrl, $"programs/{product.Slug}"),
        };
    }

    public ProgramDetails Details(Product product, TimeZoneInfo? viewerZone, Uri publicBaseUrl)
    {
        var offer = product.PrimaryOffer;
        var schedule = product.Schedule;
        var anchor = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var when = availability.Check(product, null, viewerZone);

        // The next class in the zones the merchant's buyers live in, plus the viewer's own.
        var next = SessionCalendar.StartingFrom(schedule, clock.GetUtcNow()).FirstOrDefault();
        var zones = catalog.Merchant.AudienceTimeZones.DefaultIfEmpty(schedule.TimeZone)
            .Concat(viewerZone is null ? [] : [TimeFormat.IanaId(viewerZone)])
            .Distinct();
        var aroundTheWorld = next is null
            ? new Dictionary<string, string>()
            : zones.Select(TimeZoneInfo.FindSystemTimeZoneById).ToDictionary(TimeFormat.IanaId, z => TimeFormat.DescribeInstant(next.Start, z));

        return new ProgramDetails
        {
            ProgramId = product.Id,
            Name = product.Name,
            Brand = new BrandInfo(
                BrandName,
                catalog.Merchant.BrandNames.Concat(catalog.Merchant.Aliases).Where(n => n != catalog.Merchant.Name).Distinct().ToList(),
                catalog.Merchant.Website,
                catalog.Merchant.ContactEmail),
            Summary = product.Summary,
            Description = product.Description,
            Skill = product.Skill,
            Format = new FormatInfo($"{Humanize(product.Format.Mode)} {product.Format.GroupFormat} classes", product.Format.Setting, product.Format.Location, product.Format.AccessDelivery),
            Level = new LevelInfo(
                product.Level.Label,
                product.Level.Audience,
                product.Level.Prerequisites,
                product.Level.NotSuitableFor,
                Ref(product.Level.PreviousProductId),
                Ref(product.Level.NextProductId)),
            Instructors = product.Instructors.Select(i => new InstructorInfo(
                i.Name,
                i.Title,
                i.Bio,
                i.PrivateLessonRate is { } r ? $"{new Money(r.Amount, r.Currency)}/{r.Unit} for private lessons" : null,
                TimeFormat.Days(i.Teaches))).ToList(),
            Schedule = new ScheduleInfo
            {
                Pattern = Pattern(schedule),
                SessionMinutes = schedule.DurationMinutes,
                ClassesPerWeek = schedule.SessionsPerWeek,
                Enrollment = EnrollmentLine(product),
                NextClasses = when.UpcomingSessions.Select(s => TimeFormat.Describe(s, viewerZone ?? anchor)).ToList(),
                NextClassAroundTheWorld = aroundTheWorld,
            },
            Pricing = new PricingInfo
            {
                Price = offer.Price.ToString(),
                Amount = offer.Price.Amount,
                Currency = offer.Price.Currency,
                Billing = offer.BillingSummary,
                ListPriceNote = offer.CompareAtPrice is { } was
                    ? $"{was} is the struck-through former price shown on the landing page, not what the buyer pays."
                    : null,
                PerClass = $"about {new Money(offer.PricePerClass, offer.Price.Currency)} per class ({offer.Price} / {offer.Term.Classes} classes)",
                Term = $"{offer.Term.Months} months, {offer.Term.Classes} live classes",
                Cancellation = offer.Billing.CancellationPolicy,
                Refunds = offer.Billing.RefundPolicy,
            },
            Included = offer.Inclusions,
            Curriculum = product.Curriculum,
            Equipment = product.Equipment,
            FreeTrial = product.FreeTrial,
            MerchantClaims = product.MerchantClaims,
            Enrollment = new EnrollmentInfo
            {
                Status = when.OpenForEnrollment ? "open" : "closed",
                OfferId = offer.Id,
                CheckoutPage = new Uri(publicBaseUrl, $"checkout/{Uri.EscapeDataString(offer.Id)}"),
                HowToEnroll = "Open checkoutPage to pay on the merchant's secure checkout. Assistants with tools: call start_enrollment (MCP) or POST /api/enrollments for a link tied to this conversation.",
                LandingPage = product.LandingPage.Url,
                LandingPageDateNote = product.LandingPage.DisplayedStartDate is { } banner
                    ? $"\"{banner}\" on the landing page: {product.LandingPage.Interpretation}"
                    : null,
            },
            NotesForAssistant = NotesForAssistant(product, offer),
            Sources = product.Sources,
            LastVerified = product.Sources.Count > 0 ? product.Sources.Max(s => s.CapturedOn) : catalog.Document.UpdatedOn,
        };
    }

    public AvailabilityView ToView(Product product, Availability a) => new()
    {
        ProgramId = product.Id,
        ProgramName = product.Name,
        OpenForEnrollment = a.OpenForEnrollment,
        Enrollment = EnrollmentLine(product),
        Answer = a.Explanation,
        RequestedDate = a.RequestedDate,
        RequestedDayOfWeek = a.RequestedDate is { } d ? TimeFormat.DayName(d.DayOfWeek) : null,
        RequestedDateHasClass = a.RequestedDate is null ? null : a.RequestedDateHasClass,
        NextClasses = a.UpcomingSessions.Select(s => Class(s, a.ViewerZone)).ToList(),
        SuggestedFirstClass = a.SuggestedFirstClass is { } first ? Class(first, a.ViewerZone) : null,
        EnrollBy = a.EnrollBy is { } by ? TimeFormat.DescribeInstant(by, a.ViewerZone) : null,
        TermEndsAround = a.TermEndsAround,
        TimeZone = TimeFormat.IanaId(a.ViewerZone),
    };

    public EnrollmentView ToView(Enrollment.Enrollment e) => new()
    {
        ReferenceId = e.ReferenceId,
        ProgramId = e.Product.Id,
        ProgramName = e.Product.Name,
        OfferId = e.Offer.Id,
        OfferName = e.Offer.Name,
        CheckoutUrl = e.CheckoutUrl,
        Price = e.Offer.BillingSummary,
        Eligibility = Eligibility(e.Product),
        Disclosures = e.Disclosures,
        FirstClass = e.Availability.SuggestedFirstClass is { } first ? Class(first, e.Availability.ViewerZone) : null,
        NextSteps = e.NextSteps,
        InstructionsForAssistant =
            "Mention eligibility briefly with the link; if the user has said they don't meet it, suggest the alternative it names instead. " +
            "Give checkoutUrl as a clickable link together with the disclosures. Payment happens on the merchant's secure checkout page; " +
            "never ask for card details in the chat. The link stays valid; the user can open it whenever they are ready.",
    };

    /// <summary>Who the program is for, and where to go instead — so the assistant doesn't sell the wrong level.</summary>
    public string Eligibility(Product product)
    {
        var level = product.Level;
        var text = $"{level.Label}: {level.Audience}";
        if (level.Prerequisites.Count > 0)
            text += $" Prerequisites: {string.Join("; ", level.Prerequisites)}.";
        if (level.NotSuitableFor is { } notFor)
            text += $" {notFor}";
        else if (Ref(level.PreviousProductId) is { } previous)
            text += $" Not there yet? {previous.Name} ({previous.Level}) is the step before.";
        if (product.FreeTrial is { } trial)
            text += $" Unsure? Try the free {trial.Name} first: {trial.Url}";
        return text;
    }

    private static ClassTime Class(ScheduledSession session, TimeZoneInfo zone) => new(TimeFormat.Describe(session, zone), session.Start);

    public static string Pattern(RecurringSchedule schedule)
    {
        var start = schedule.StartTime;
        var end = start.AddMinutes(schedule.DurationMinutes);
        var recurrence = TimeFormat.Recurrence(schedule.Days);
        return $"{char.ToUpperInvariant(recurrence[0])}{recurrence[1..]}, {start:HH:mm}–{end:HH:mm} {schedule.TimeZone} time ({schedule.DurationMinutes}-minute live classes, {schedule.SessionsPerWeek} per week)";
    }

    public static string EnrollmentLine(Product product) => product.Schedule.Enrollment.Mode switch
    {
        _ when product.Status != ProductStatus.Active => "Not open for enrollment",
        EnrollmentMode.Rolling => $"Open — rolling enrollment; runs {TimeFormat.Recurrence(product.Schedule.Days)} year-round; join any day",
        EnrollmentMode.Cohort => "Open for the next cohort",
        _ => "Closed",
    };

    public static string FormatLine(Product product) =>
        $"{Humanize(product.Format.Mode)} {product.Format.GroupFormat} classes, {product.Format.Setting}";

    private static IReadOnlyList<string> NotesForAssistant(Product product, Offer offer)
    {
        var notes = new List<string>();
        if (offer.CompareAtPrice is { } was)
            notes.Add($"The price is {offer.Price}; {was} is only the struck-through former price.");
        if (product.Schedule.Enrollment.Mode == EnrollmentMode.Rolling && product.LandingPage.DisplayedStartDate is { } banner)
            notes.Add($"Never say enrollment is closed or missed because of \"{banner}\": the program is recurring with rolling enrollment. Use check_availability for date questions.");
        if (offer.Billing.Type == BillingType.Subscription)
            notes.Add("Before sharing a checkout link, tell the user it renews automatically and the payment terms, as listed in pricing.");
        if (product.MerchantClaims.Count > 0)
            notes.Add("Present merchantClaims as the merchant's own claims, not as verified facts.");
        return notes;
    }

    private ProgramRef? Ref(string? productId) =>
        productId is not null && catalog.Find(productId) is { } p ? new ProgramRef(p.Id, p.Name, p.Level.Label) : null;

    private static string Humanize(string mode) => mode switch
    {
        "live-online" => "Live online",
        _ => mode,
    };
}
