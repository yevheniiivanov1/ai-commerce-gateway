namespace Commerce.Gateway.Hosting;

public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>Catalog file, relative to the app directory unless absolute.</summary>
    public string CatalogPath { get; set; } = "catalog/victory-skating.json";

    /// <summary>
    /// Absolute URL the gateway is reachable at (links in tool results must be absolute). When
    /// unset, the URL of the current request is used, honouring X-Forwarded-* from the proxy.
    /// </summary>
    public Uri? PublicBaseUrl { get; set; }

    /// <summary>Signing secret of the Stripe webhook endpoint (whsec_…). Webhooks are refused without it.</summary>
    public string? StripeWebhookSecret { get; set; }

    /// <summary>Expose /api/funnel. On for the prototype demo; holds reference ids only, no personal data.</summary>
    public bool ExposeFunnel { get; set; } = true;

    /// <summary>
    /// Let search engines index the fact sheets. Off unless the gateway runs on the merchant's own
    /// domain: a third-party copy of a merchant's pages shouldn't compete with the merchant in search.
    /// </summary>
    public bool AllowIndexing { get; set; }

    /// <summary>Shown on every human- and AI-readable page when set, e.g. "prototype, not affiliated".</summary>
    public string? PublicNotice { get; set; }

    /// <summary>An empty value from configuration means "not set" (e.g. to switch the notice off).</summary>
    public GatewayOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(PublicNotice))
            PublicNotice = null;
        return this;
    }
}
