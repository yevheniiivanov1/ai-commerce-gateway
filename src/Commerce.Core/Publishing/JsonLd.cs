using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Commerce.Core.Catalog;
using Commerce.Core.Scheduling;

namespace Commerce.Core.Publishing;

/// <summary>
/// schema.org markup for crawlers and answer engines, generated from the same catalog as the MCP
/// tools. Two details target exactly what the baseline got wrong: the $699 "was" price is typed
/// as a StrikethroughPrice, and the timetable is a weekly <c>Schedule</c>, not a start date.
/// </summary>
public static class JsonLd
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ForProduct(Catalog.Catalog catalog, Product product, DateTimeOffset now) =>
        Prune(new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new JsonArray(Organization(catalog.Merchant), Course(catalog.Merchant, product, now), Faq(product)),
        }).ToJsonString(Indented);

    /// <summary>The snippet a merchant pastes into their page head (e.g. Tilda → Page settings → HTML head).</summary>
    public static string ScriptTag(Catalog.Catalog catalog, Product product, DateTimeOffset now) =>
        $"<script type=\"application/ld+json\">\n{ForProduct(catalog, product, now)}\n</script>";

    private static string OrgId(Merchant merchant) => new Uri(merchant.Website, "#organization").ToString();

    private static JsonObject Organization(Merchant merchant) => new()
    {
        ["@type"] = "Organization",
        ["@id"] = OrgId(merchant),
        ["name"] = merchant.Name,
        ["alternateName"] = Array(merchant.BrandNames.Concat(merchant.Aliases).Where(n => n != merchant.Name)),
        ["description"] = merchant.Description,
        ["url"] = merchant.Website.ToString(),
        ["email"] = merchant.ContactEmail,
        ["sameAs"] = Array(merchant.RelatedSites.Select(u => u.ToString())),
    };

    private static JsonObject Course(Merchant merchant, Product product, DateTimeOffset now)
    {
        var schedule = product.Schedule;
        var next = SessionCalendar.StartingFrom(schedule, now).First();
        var anchor = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);

        return new JsonObject
        {
            ["@type"] = "Course",
            ["@id"] = new Uri(product.LandingPage.Url, "#course").ToString(),
            ["name"] = product.Name,
            ["alternateName"] = Array(product.Aliases.Prepend(product.ShortName)),
            ["description"] = product.Summary + " " + product.Description,
            ["url"] = product.LandingPage.Url.ToString(),
            ["provider"] = new JsonObject { ["@id"] = OrgId(merchant) },
            ["teaches"] = product.Skill,
            ["educationalLevel"] = $"{product.Level.Label}: {product.Level.Audience}",
            ["coursePrerequisites"] = Array(product.Level.Prerequisites),
            ["inLanguage"] = "en",
            ["keywords"] = string.Join(", ", product.Keywords),
            ["hasCourseInstance"] = new JsonArray(new JsonObject
            {
                ["@type"] = "CourseInstance",
                ["courseMode"] = "online",
                ["location"] = new JsonObject { ["@type"] = "VirtualLocation", ["description"] = product.Format.Location },
                ["courseWorkload"] = $"PT{schedule.DurationMinutes}M per class, {schedule.SessionsPerWeek} classes per week",
                ["instructor"] = new JsonArray(product.Instructors.Select(i => (JsonNode)new JsonObject
                {
                    ["@type"] = "Person",
                    ["name"] = i.Name,
                    ["jobTitle"] = i.Title,
                    ["description"] = i.Bio,
                }).ToArray()),
                ["courseSchedule"] = new JsonObject
                {
                    ["@type"] = "Schedule",
                    ["repeatFrequency"] = "P1W",
                    ["byDay"] = Array(schedule.Days.Select(d => $"https://schema.org/{d}")),
                    ["startTime"] = schedule.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                    ["endTime"] = schedule.StartTime.AddMinutes(schedule.DurationMinutes).ToString("HH:mm", CultureInfo.InvariantCulture),
                    ["duration"] = $"PT{schedule.DurationMinutes}M",
                    ["scheduleTimezone"] = schedule.TimeZone,
                    // Next class, recomputed on every render — never a stale cohort date.
                    ["startDate"] = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(next.Start, anchor).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
            }),
            ["offers"] = new JsonArray(product.Offers.Select(o => (JsonNode)Offer(merchant, product, o)).ToArray()),
        };
    }

    private static JsonObject Offer(Merchant merchant, Product product, Offer offer)
    {
        var specs = new JsonArray(new JsonObject
        {
            ["@type"] = "UnitPriceSpecification",
            ["price"] = offer.Price.Amount,
            ["priceCurrency"] = offer.Price.Currency,
            ["billingDuration"] = offer.Billing.IntervalMonths is { } m ? $"P{m}M" : null,
            ["description"] = offer.BillingSummary,
        });
        if (offer.CompareAtPrice is { } was)
        {
            specs.Add(new JsonObject
            {
                ["@type"] = "UnitPriceSpecification",
                ["priceType"] = "https://schema.org/StrikethroughPrice",
                ["price"] = was.Amount,
                ["priceCurrency"] = was.Currency,
            });
        }

        return new JsonObject
        {
            ["@type"] = "Offer",
            ["name"] = offer.Name,
            ["category"] = offer.Billing.Type == BillingType.Subscription ? "Subscription" : "One-time purchase",
            ["price"] = offer.Price.Amount,
            ["priceCurrency"] = offer.Price.Currency,
            ["priceSpecification"] = specs,
            ["availability"] = offer.Availability == OfferAvailability.Open ? "https://schema.org/InStock" : "https://schema.org/SoldOut",
            ["url"] = product.LandingPage.Url.ToString(),
            ["description"] = $"{offer.Term.Months} months, {offer.Term.Classes} live classes. " + string.Join("; ", offer.Inclusions),
            ["seller"] = new JsonObject { ["@id"] = OrgId(merchant) },
        };
    }

    private static JsonObject Faq(Product product)
    {
        var offer = product.Offers[0];
        var schedule = product.Schedule;
        var days = string.Join(" and ", schedule.Days.Select(TimeFormat.DayName));
        var qa = new List<(string Q, string A)>
        {
            ($"How much does the {product.ShortName} cost?",
                $"{offer.BillingSummary}. That covers {offer.Term.Months} months and {offer.Term.Classes} live classes." +
                (offer.CompareAtPrice is { } was ? $" {was} is the former price." : "")),
            ($"When are the {product.ShortName} classes?", Facts.ProgramFacts.Pattern(schedule) + "."),
            ($"Can I join the {product.ShortName} after the date shown on the page?",
                schedule.Enrollment.Mode == EnrollmentMode.Rolling
                    ? $"Yes. The club runs every {days}, year-round. Enroll on any day; session links arrive within {schedule.Enrollment.AccessLeadTimeHours} hours and you start the next weekend."
                    : "Enrollment follows fixed cohort dates."),
            ($"Who is the {product.ShortName} for?", product.Level.Audience + (product.Level.NotSuitableFor is { } not ? " " + not : "")),
            ($"Who coaches the {product.ShortName}?", string.Join(" ", product.Instructors.Select(i => $"{i.Title} {i.Name}: {i.Bio}"))),
            ($"Is the {product.ShortName} on-ice or off-ice?", $"{Facts.ProgramFacts.FormatLine(product)}. You need: {string.Join(", ", product.Equipment).ToLowerInvariant()}."),
        };
        if (offer.Billing.CancellationPolicy is { } cancel)
            qa.Add(("How do I cancel?", cancel + (offer.Billing.RefundPolicy is { } r ? " Refunds: " + r : "")));

        return new JsonObject
        {
            ["@type"] = "FAQPage",
            ["mainEntity"] = new JsonArray(qa.Select(x => (JsonNode)new JsonObject
            {
                ["@type"] = "Question",
                ["name"] = x.Q,
                ["acceptedAnswer"] = new JsonObject { ["@type"] = "Answer", ["text"] = x.A },
            }).ToArray()),
        };
    }

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    /// <summary>Drops null properties and empty arrays — optional catalog fields simply don't appear.</summary>
    private static JsonNode Prune(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is null || value is JsonArray { Count: 0 })
                        obj.Remove(key);
                    else
                        Prune(value);
                }
                break;
            case JsonArray array:
                foreach (var item in array.OfType<JsonNode>())
                    Prune(item);
                break;
        }
        return node;
    }
}
