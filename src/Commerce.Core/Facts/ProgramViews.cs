using Commerce.Core.Catalog;

namespace Commerce.Core.Facts;

// The AI-facing contract. Same shapes for MCP tool results and the REST API, so every assistant
// gets identical facts. Prices, schedules and policies are pre-rendered as sentences: an assistant
// repeats a sentence far more reliably than it reconstructs one from raw fields.

public sealed record ProgramRef(string ProgramId, string Name, string Level);

public sealed record ProgramCard
{
    public required string ProgramId { get; init; }
    public required string Name { get; init; }
    public required string Brand { get; init; }
    public required string Skill { get; init; }
    public required string Level { get; init; }
    public required string Format { get; init; }
    public required string Price { get; init; }
    public required decimal PriceAmount { get; init; }
    public required string Currency { get; init; }
    public required string Duration { get; init; }
    public required string Schedule { get; init; }
    public required string Instructors { get; init; }
    public required string Summary { get; init; }
    public required string Enrollment { get; init; }
    /// <summary>A no-commitment way to try it first, when the merchant has one.</summary>
    public string? FreeTrial { get; init; }
    public required string OfferId { get; init; }
    public bool? WithinBudget { get; init; }
    public IReadOnlyList<string> WhyItMatches { get; init; } = [];
    public required Uri FactSheet { get; init; }
}

public sealed record ProgramDetails
{
    public required string ProgramId { get; init; }
    public required string Name { get; init; }
    public required BrandInfo Brand { get; init; }
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public required string Skill { get; init; }
    public required FormatInfo Format { get; init; }
    public required LevelInfo Level { get; init; }
    public required IReadOnlyList<InstructorInfo> Instructors { get; init; }
    public required ScheduleInfo Schedule { get; init; }
    public required PricingInfo Pricing { get; init; }
    public required IReadOnlyList<string> Included { get; init; }
    public required IReadOnlyList<CurriculumModule> Curriculum { get; init; }
    public required IReadOnlyList<string> Equipment { get; init; }
    public FreeTrial? FreeTrial { get; init; }
    public IReadOnlyList<string> MerchantClaims { get; init; } = [];
    public required EnrollmentInfo Enrollment { get; init; }
    public required IReadOnlyList<string> NotesForAssistant { get; init; }
    public required IReadOnlyList<SourceRef> Sources { get; init; }
    public required DateOnly LastVerified { get; init; }
}

public sealed record BrandInfo(string Name, IReadOnlyList<string> AlsoKnownAs, Uri Website, string? ContactEmail);

public sealed record FormatInfo(string Mode, string Setting, string? Where, string? Access);

public sealed record LevelInfo(
    string Label,
    string ForWho,
    IReadOnlyList<string> Prerequisites,
    string? NotSuitableFor,
    ProgramRef? PreviousLevel,
    ProgramRef? NextLevel);

public sealed record InstructorInfo(string Name, string Title, string? Bio, string? PrivateLessonRate, string Teaches);

public sealed record ScheduleInfo
{
    public required string Pattern { get; init; }
    public required int SessionMinutes { get; init; }
    public required int ClassesPerWeek { get; init; }
    public required string Enrollment { get; init; }
    /// <summary>The next few classes in the viewer's zone (or the organiser's).</summary>
    public required IReadOnlyList<string> NextClasses { get; init; }
    /// <summary>Start time of the next class around the world — DST-correct, unlike a static table.</summary>
    public required IReadOnlyDictionary<string, string> NextClassAroundTheWorld { get; init; }
}

public sealed record PricingInfo
{
    public required string Price { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string Billing { get; init; }
    public string? ListPriceNote { get; init; }
    public required string PerClass { get; init; }
    public required string Term { get; init; }
    public string? Cancellation { get; init; }
    public string? Refunds { get; init; }
}

public sealed record SearchProgramsResult(string Merchant, IReadOnlyList<ProgramCard> Programs, string? Note);

public sealed record ClassTime(string Local, DateTimeOffset StartsAtUtc);

public sealed record AvailabilityView
{
    public required string ProgramId { get; init; }
    public required string ProgramName { get; init; }
    public required bool OpenForEnrollment { get; init; }
    public required string Enrollment { get; init; }
    /// <summary>The direct answer, ready to relay.</summary>
    public required string Answer { get; init; }
    public DateOnly? RequestedDate { get; init; }
    public string? RequestedDayOfWeek { get; init; }
    public bool? RequestedDateHasClass { get; init; }
    public required IReadOnlyList<ClassTime> NextClasses { get; init; }
    public ClassTime? SuggestedFirstClass { get; init; }
    public string? EnrollBy { get; init; }
    public DateOnly? TermEndsAround { get; init; }
    public required string TimeZone { get; init; }
}

public sealed record EnrollmentView
{
    public required string ReferenceId { get; init; }
    public required string ProgramId { get; init; }
    public required string ProgramName { get; init; }
    public required string OfferId { get; init; }
    public required string OfferName { get; init; }
    public required Uri CheckoutUrl { get; init; }
    public required string Price { get; init; }
    /// <summary>Who the program is for and where to go instead, to state with the link.</summary>
    public required string Eligibility { get; init; }
    /// <summary>Terms to state before the buyer pays.</summary>
    public required IReadOnlyList<string> Disclosures { get; init; }
    public ClassTime? FirstClass { get; init; }
    public required IReadOnlyList<string> NextSteps { get; init; }
    public required string InstructionsForAssistant { get; init; }
}

public sealed record EnrollmentInfo
{
    public required string Status { get; init; }
    public required string OfferId { get; init; }
    /// <summary>Plain link to the merchant's checkout for readers that can't call tools.</summary>
    public required Uri CheckoutPage { get; init; }
    public required string HowToEnroll { get; init; }
    public required Uri LandingPage { get; init; }
    public string? LandingPageDateNote { get; init; }
}
