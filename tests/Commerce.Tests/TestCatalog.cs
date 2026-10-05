using Commerce.Core.Catalog;
using Commerce.Core.Checkout;
using Microsoft.Extensions.Time.Testing;

namespace Commerce.Tests;

/// <summary>The shipped catalog plus a clock pinned to the evening the baseline was captured.</summary>
public static class TestCatalog
{
    /// <summary>Monday 2026-10-05, 20:00 UTC — the landing page says "Join us on October 10".</summary>
    public static readonly DateTimeOffset BaselineEvening = new(2026, 10, 5, 20, 0, 0, TimeSpan.Zero);

    public static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "catalog", "victory-skating.json");

    public static CatalogDocument LoadDocument() =>
        new JsonFileCatalogSource(CatalogPath).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

    public static Catalog Load() => new(LoadDocument());

    public static FakeTimeProvider Clock(DateTimeOffset? at = null) => new(at ?? BaselineEvening);

    public static CheckoutProviderRegistry Providers() => new([new StripePaymentLinkProvider(), new PlainLinkProvider()]);

    public static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
}
