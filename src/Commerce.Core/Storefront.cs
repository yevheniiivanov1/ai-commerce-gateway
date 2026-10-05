using Commerce.Core.Catalog;
using Commerce.Core.Enrollment;
using Commerce.Core.Facts;
using Commerce.Core.Scheduling;
using Commerce.Core.Search;

namespace Commerce.Core;

/// <summary>A request an assistant can fix and retry: unknown id, unreadable date or time zone.</summary>
public sealed class StorefrontException(string message, bool notFound = false) : Exception(message)
{
    public bool NotFound { get; } = notFound;
}

/// <summary>
/// The four operations every channel exposes — discover, inspect, check a date, enroll. MCP tools
/// and REST endpoints are thin adapters over this, so their answers can't drift apart.
/// </summary>
public sealed class Storefront(
    Catalog.Catalog catalog,
    ProductSearch search,
    ProgramFacts facts,
    AvailabilityService availability,
    EnrollmentService enrollments,
    TimeProvider clock)
{
    public SearchProgramsResult Search(string? query, decimal? maxPrice, string? currency, Uri publicBaseUrl)
    {
        var result = search.Search(new SearchRequest(query, maxPrice, currency));
        var cards = result.Matches.Select(m => facts.Card(m, publicBaseUrl)).ToList();

        string? note = null;
        if (result.QueryMatchedNothing)
            note = $"No program matched \"{query}\". These are all programs {catalog.Merchant.Name} offers.";
        else if (maxPrice is not null && cards.Count > 0 && cards.All(c => c.WithinBudget == false))
            note = FormattableString.Invariant($"No program fits within {maxPrice:0.##}; the most affordable is {cards.MinBy(c => c.PriceAmount)!.Price}.");

        return new SearchProgramsResult(facts.BrandName, cards, note);
    }

    public ProgramDetails Details(string programId, string? timeZone, Uri publicBaseUrl) =>
        facts.Details(Product(programId), Zone(timeZone), publicBaseUrl);

    public AvailabilityView Availability(string programId, string? date, string? timeZone)
    {
        var product = Product(programId);
        var zone = Zone(timeZone);
        return facts.ToView(product, availability.Check(product, Date(date, zone), zone));
    }

    public EnrollmentView Enroll(string programId, string? offerId, string? preferredStartDate, string? timeZone, string channel)
    {
        var product = Product(programId);
        var zone = Zone(timeZone);
        var outcome = enrollments.Start(new EnrollmentRequest(product.Id, offerId, Date(preferredStartDate, zone), zone, channel));
        if (outcome.Enrollment is not { } enrollment)
            throw new StorefrontException(outcome.Message!, outcome.Error == EnrollmentError.UnknownOffer);
        return ProgramFacts.ToView(enrollment);
    }

    private Product Product(string programId) =>
        catalog.Find(programId.Trim())
        ?? throw new StorefrontException(
            $"Unknown programId '{programId}'. Valid ids: {string.Join(", ", catalog.Listed.Select(p => p.Id))}.",
            notFound: true);

    private static TimeZoneInfo? Zone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
            return null;
        return TimeFormat.TryResolveZone(timeZone, out var zone)
            ? zone
            : throw new StorefrontException($"Unknown time zone '{timeZone}'. Use an IANA id such as America/New_York or Europe/London.");
    }

    private DateOnly? Date(string? value, TimeZoneInfo? zone)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone ?? TimeZoneInfo.Utc).DateTime);
        return TimeFormat.TryParseDate(value, today, out var date)
            ? date
            : throw new StorefrontException($"Couldn't read the date '{value}'. Use YYYY-MM-DD, e.g. {today:yyyy-MM-dd}.");
    }
}
