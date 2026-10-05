using System.Text.RegularExpressions;
using Commerce.Core.Catalog;

namespace Commerce.Core.Checkout;

/// <param name="ReferenceId">Our enrollment id; the payment platform echoes it back on completion.</param>
/// <param name="Channel">Which assistant or surface sent the buyer, e.g. "perplexity".</param>
public sealed record CheckoutContext(string ReferenceId, string Channel);

/// <summary>
/// Turns an offer into the URL where the buyer pays. One implementation per payment platform;
/// the catalog names the provider per offer, so one merchant can mix platforms.
/// </summary>
public interface ICheckoutProvider
{
    string Name { get; }
    Uri CreateCheckoutUrl(Offer offer, CheckoutContext context);
}

/// <summary>
/// Stripe Payment Links: <c>client_reference_id</c> comes back in the
/// <c>checkout.session.completed</c> webhook and UTM codes are recorded on the payment
/// (https://docs.stripe.com/payment-links/url-parameters), so an AI-originated sale is attributable
/// without changing anything in the merchant's Stripe setup.
/// </summary>
public sealed class StripePaymentLinkProvider : ICheckoutProvider
{
    public string Name => "stripe-payment-link";

    public Uri CreateCheckoutUrl(Offer offer, CheckoutContext context) =>
        Url.WithQuery(offer.Checkout.Url,
        [
            ("client_reference_id", context.ReferenceId),
            ("utm_source", context.Channel),
            ("utm_medium", "ai-assistant"),
            ("utm_campaign", "agentic-enrollment"),
        ]);
}

/// <summary>
/// A checkout or booking page that can't carry our reference (a site-builder cart, a booking
/// widget). Only campaign codes are added; attribution then relies on the platform's analytics.
/// </summary>
public sealed class PlainLinkProvider : ICheckoutProvider
{
    public string Name => "link";

    public Uri CreateCheckoutUrl(Offer offer, CheckoutContext context) =>
        Url.WithQuery(offer.Checkout.Url, [("utm_source", context.Channel), ("utm_medium", "ai-assistant")]);
}

public sealed partial class CheckoutProviderRegistry(IEnumerable<ICheckoutProvider> providers)
{
    private readonly Dictionary<string, ICheckoutProvider> _byName = providers.ToDictionary(p => p.Name, StringComparer.Ordinal);

    public IReadOnlySet<string> Names => _byName.Keys.ToHashSet();

    public Uri CreateCheckoutUrl(Offer offer, CheckoutContext context)
    {
        if (!IsValidReference(context.ReferenceId))
            throw new ArgumentException("Reference ids are 1–200 letters, digits, dashes or underscores.", nameof(context));
        return _byName[offer.Checkout.Provider].CreateCheckoutUrl(offer, context);
    }

    /// <summary>Stripe's rule for client_reference_id, applied to every provider.</summary>
    public static bool IsValidReference(string? value) => value is not null && ReferencePattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,200}$")]
    private static partial Regex ReferencePattern();
}

internal static class Url
{
    public static Uri WithQuery(Uri url, IEnumerable<(string Key, string Value)> parameters)
    {
        var builder = new UriBuilder(url);
        var existing = builder.Query.TrimStart('?');
        var added = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        builder.Query = string.IsNullOrEmpty(existing) ? added : $"{existing}&{added}";
        return builder.Uri;
    }
}
