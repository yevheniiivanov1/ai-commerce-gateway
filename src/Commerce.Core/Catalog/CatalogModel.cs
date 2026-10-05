using System.Text.Json.Serialization;

namespace Commerce.Core.Catalog;

// The canonical, platform-independent product model. Every channel (MCP tools, REST, JSON-LD,
// llms.txt) is generated from this, and every source platform (a hand-curated JSON file today;
// Shopify, WooCommerce, Mindbody… tomorrow) is mapped into it.

public sealed record CatalogDocument
{
    public required string SchemaVersion { get; init; }
    public required DateOnly UpdatedOn { get; init; }
    public required Merchant Merchant { get; init; }
    public SearchSettings Search { get; init; } = new();
    public required IReadOnlyList<Product> Products { get; init; }
}

public sealed record SearchSettings
{
    /// <summary>Canonical phrase → the shorthand buyers type for it ("double axel" ← "2a", "2 axel").</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
}

public sealed record Merchant
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Names the business trades under; all of them should resolve to this merchant.</summary>
    public required IReadOnlyList<string> BrandNames { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    /// <summary>What the merchant sells, as a noun phrase: "live online yoga classes".</summary>
    public required string Offering { get; init; }
    public required string Description { get; init; }
    public required Uri Website { get; init; }
    public IReadOnlyList<Uri> RelatedSites { get; init; } = [];
    public string? ContactEmail { get; init; }
    public Uri? TermsUrl { get; init; }
    public Uri? PrivacyUrl { get; init; }
    public Uri? AppUrl { get; init; }
    /// <summary>Where buyers mostly are: class times are also shown in these zones.</summary>
    public IReadOnlyList<string> AudienceTimeZones { get; init; } = [];
}

public enum ProductStatus { Active, Paused, Retired }

public sealed record Product
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public required string Category { get; init; }
    public required string Skill { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public required DeliveryFormat Format { get; init; }
    public required SkillLevel Level { get; init; }
    public IReadOnlyList<Instructor> Instructors { get; init; } = [];
    public required RecurringSchedule Schedule { get; init; }
    public IReadOnlyList<CurriculumModule> Curriculum { get; init; } = [];
    public IReadOnlyList<string> Equipment { get; init; } = [];
    public required IReadOnlyList<Offer> Offers { get; init; }
    public FreeTrial? FreeTrial { get; init; }
    /// <summary>Marketing statements made by the merchant — passed on as claims, never as facts.</summary>
    public IReadOnlyList<string> MerchantClaims { get; init; } = [];
    public required LandingPage LandingPage { get; init; }
    public IReadOnlyList<SourceRef> Sources { get; init; } = [];
    /// <summary>Open questions for the merchant. Internal: never exposed to AI channels.</summary>
    public IReadOnlyList<string> ReviewNotes { get; init; } = [];
    public ProductStatus Status { get; init; } = ProductStatus.Active;

    /// <summary>The offer every channel quotes: the first open one, else the first listed.</summary>
    [JsonIgnore]
    public Offer PrimaryOffer => Offers.FirstOrDefault(o => o.Availability == OfferAvailability.Open) ?? Offers[0];
}

public sealed record DeliveryFormat
{
    public required string Mode { get; init; }
    public required string Setting { get; init; }
    public required string GroupFormat { get; init; }
    public string? Location { get; init; }
    public string? AccessDelivery { get; init; }
}

public sealed record SkillLevel
{
    public required string Label { get; init; }
    public required int Rank { get; init; }
    public required string Audience { get; init; }
    public IReadOnlyList<string> Prerequisites { get; init; } = [];
    public string? NotSuitableFor { get; init; }
    public string? PreviousProductId { get; init; }
    public string? NextProductId { get; init; }
}

public sealed record Instructor
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public string? Bio { get; init; }
    public Rate? PrivateLessonRate { get; init; }
    public IReadOnlyList<DayOfWeek> Teaches { get; init; } = [];
}

public sealed record Rate(decimal Amount, string Currency, string Unit);

public sealed record CurriculumModule(string Title, IReadOnlyList<string> Points);

public sealed record FreeTrial
{
    public required string Name { get; init; }
    public required Uri Url { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// What the landing page shows, kept apart from the business rules. A "Join us on October 10"
/// banner is a marketing artefact; availability comes from <see cref="RecurringSchedule"/>.
/// </summary>
public sealed record LandingPage
{
    public required Uri Url { get; init; }
    public string? DisplayedStartDate { get; init; }
    public string? Interpretation { get; init; }
}

public sealed record SourceRef(Uri Url, DateOnly CapturedOn, string Covers);
