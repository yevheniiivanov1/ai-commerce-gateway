using Commerce.Core.Checkout;

namespace Commerce.Core.Catalog;

/// <summary>
/// Rejects a catalog an assistant could misquote: wrong references, prices it can't compare,
/// schedules it can't compute, checkouts nobody can open. Runs at startup — bad data never serves.
/// </summary>
public static class CatalogValidator
{
    public static IReadOnlyList<string> Validate(CatalogDocument catalog, IReadOnlySet<string> checkoutProviders)
    {
        var errors = new List<string>();
        var productIds = catalog.Products.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        if (catalog.Merchant.BrandNames.Count == 0)
            errors.Add("merchant: at least one brand name is required");
        if (catalog.Products.Count == 0)
            errors.Add("catalog has no products");

        Duplicates(catalog.Products.Select(p => p.Id), "product id", errors);
        Duplicates(catalog.Products.Select(p => p.Slug), "product slug", errors);
        Duplicates(catalog.Products.SelectMany(p => p.Offers).Select(o => o.Id), "offer id", errors);

        foreach (var product in catalog.Products)
        {
            var at = $"product '{product.Id}'";

            if (!IsSlug(product.Slug))
                errors.Add($"{at}: slug '{product.Slug}' must be lowercase letters, digits and dashes");
            foreach (var reference in new[] { product.Level.PreviousProductId, product.Level.NextProductId })
                if (reference is not null && !productIds.Contains(reference))
                    errors.Add($"{at}: level references unknown product '{reference}'");

            var schedule = product.Schedule;
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZone, out _))
                errors.Add($"{at}: unknown time zone '{schedule.TimeZone}'");
            if (schedule.Days.Count == 0)
                errors.Add($"{at}: schedule has no days");
            if (schedule.DurationMinutes is <= 0 or > 24 * 60)
                errors.Add($"{at}: session duration must be between 1 and 1440 minutes");
            if (schedule.Enrollment.AccessLeadTimeHours < 0)
                errors.Add($"{at}: access lead time can't be negative");

            if (product.Offers.Count == 0)
                errors.Add($"{at}: no offers — nothing to enroll in");
            foreach (var offer in product.Offers)
            {
                var o = $"{at} offer '{offer.Id}'";
                if (offer.Price.Amount <= 0)
                    errors.Add($"{o}: price must be positive");
                if (offer.Price.Currency.Length != 3 || offer.Price.Currency != offer.Price.Currency.ToUpperInvariant())
                    errors.Add($"{o}: currency must be an ISO 4217 code");
                if (offer.CompareAtPrice is { } was && (was.Currency != offer.Price.Currency || was.Amount <= offer.Price.Amount))
                    errors.Add($"{o}: compare-at price must be higher than the price, in the same currency");
                if (offer.Term.Months <= 0 || offer.Term.Classes <= 0)
                    errors.Add($"{o}: term needs positive months and classes");
                if (offer.Billing.Type == BillingType.Subscription && offer.Billing.IntervalMonths is null or <= 0)
                    errors.Add($"{o}: subscription needs a billing interval");
                if (offer.Checkout.Url.Scheme != Uri.UriSchemeHttps)
                    errors.Add($"{o}: checkout URL must be https");
                if (!checkoutProviders.Contains(offer.Checkout.Provider))
                    errors.Add($"{o}: no checkout provider named '{offer.Checkout.Provider}'");
            }
        }

        return errors;
    }

    private static void Duplicates(IEnumerable<string> values, string what, List<string> errors)
    {
        foreach (var group in values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"duplicate {what} '{group.Key}'");
    }

    private static bool IsSlug(string value) =>
        value.Length > 0 && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
