using Commerce.Core.Catalog;
using Commerce.Core.Facts;
using Commerce.Core.Publishing;
using Commerce.Core.Scheduling;

namespace Commerce.Tests;

public class CatalogTests
{
    [Fact]
    public void The_shipped_catalog_is_valid()
    {
        var errors = CatalogValidator.Validate(TestCatalog.LoadDocument(), TestCatalog.Providers().Names);

        Assert.Empty(errors);
    }

    [Fact]
    public void The_validator_rejects_data_an_assistant_could_misquote()
    {
        var document = TestCatalog.LoadDocument();
        var product = document.Products[0];
        var offer = product.Offers[0];
        var broken = document with
        {
            Products =
            [
                product with
                {
                    Level = product.Level with { NextProductId = "vsa-quad-jumps-club" },
                    Schedule = product.Schedule with { TimeZone = "Mars/Olympus_Mons" },
                    Offers =
                    [
                        offer with
                        {
                            CompareAtPrice = new Money(199, "USD"),
                            Checkout = new CheckoutTarget("paypal-button", new Uri("http://example.com/pay")),
                        },
                    ],
                },
                product,
            ],
        };

        var errors = CatalogValidator.Validate(broken, TestCatalog.Providers().Names);

        Assert.Contains(errors, e => e.Contains("duplicate product id"));
        Assert.Contains(errors, e => e.Contains("unknown product 'vsa-quad-jumps-club'"));
        Assert.Contains(errors, e => e.Contains("unknown time zone"));
        Assert.Contains(errors, e => e.Contains("compare-at price must be higher"));
        Assert.Contains(errors, e => e.Contains("checkout URL must be https"));
        Assert.Contains(errors, e => e.Contains("no checkout provider named 'paypal-button'"));
    }

    [Fact]
    public async Task Unknown_fields_fail_loudly_instead_of_being_dropped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.json");
        var json = await File.ReadAllTextAsync(TestCatalog.CatalogPath, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, json.Replace("\"schemaVersion\"", "\"startDate\": \"2026-10-10\", \"schemaVersion\""), TestContext.Current.CancellationToken);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidCatalogException>(() => new JsonFileCatalogSource(path).LoadAsync(CancellationToken.None));
            Assert.Contains("startDate", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Json_ld_types_the_former_price_and_publishes_a_weekly_schedule()
    {
        var catalog = TestCatalog.Load();

        var jsonLd = JsonLd.ForProduct(catalog, catalog.Find("double-axel-club")!, TestCatalog.BaselineEvening);

        Assert.Contains("\"@type\": \"Course\"", jsonLd);
        Assert.Contains("\"priceType\": \"https://schema.org/StrikethroughPrice\"", jsonLd);
        Assert.Contains("\"price\": 699", jsonLd);
        Assert.Contains("\"billingDuration\": \"P6M\"", jsonLd);
        Assert.Contains("\"repeatFrequency\": \"P1W\"", jsonLd);
        Assert.Contains("https://schema.org/Saturday", jsonLd);
        Assert.Contains("\"startDate\": \"2026-10-10\"", jsonLd);
        Assert.DoesNotContain("null", jsonLd);

        using var parsed = System.Text.Json.JsonDocument.Parse(jsonLd);
        var organization = parsed.RootElement.GetProperty("@graph")[0];
        Assert.Contains("VSA", organization.GetProperty("alternateName").EnumerateArray().Select(n => n.GetString()));
    }

    [Fact]
    public void The_paste_once_snippet_has_no_date_that_could_go_stale()
    {
        var catalog = TestCatalog.Load();

        var snippet = JsonLd.ScriptTag(catalog, catalog.Find("double-axel-club")!, now: null);

        Assert.StartsWith("<script type=\"application/ld+json\">", snippet);
        Assert.DoesNotContain("startDate", snippet);
        Assert.Contains("\"repeatFrequency\": \"P1W\"", snippet);
    }

    [Fact]
    public void Internal_review_notes_never_reach_an_ai_channel()
    {
        var catalog = TestCatalog.Load();
        var clock = TestCatalog.Clock();
        var facts = new ProgramFacts(catalog, new AvailabilityService(clock), clock);
        var product = catalog.Find("vsa-double-axel-club")!;

        var markdown = FactSheet.Markdown(facts.Details(product, null, new Uri("https://gateway.test/")));
        var jsonLd = JsonLd.ForProduct(catalog, product, clock.GetUtcNow());

        Assert.All(product.ReviewNotes, note =>
        {
            Assert.DoesNotContain(note, markdown);
            Assert.DoesNotContain(note, jsonLd);
        });
    }
}
