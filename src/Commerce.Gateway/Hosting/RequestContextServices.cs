using System.Text.RegularExpressions;
using Commerce.Core.Checkout;
using Commerce.Core.Enrollment;
using Microsoft.Extensions.Options;

namespace Commerce.Gateway.Hosting;

public sealed class PublicUrls(IHttpContextAccessor http, IOptions<GatewayOptions> options)
{
    public Uri Base
    {
        get
        {
            if (options.Value.PublicBaseUrl is { } configured)
                return WithTrailingSlash(configured);
            var request = http.HttpContext?.Request
                ?? throw new InvalidOperationException("Gateway:PublicBaseUrl must be set outside of an HTTP request.");
            return WithTrailingSlash(new Uri($"{request.Scheme}://{request.Host}{request.PathBase}"));
        }
    }

    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}

/// <summary>
/// Checkout links on the gateway's own domain: <c>/checkout/{offer}?ref=…&amp;channel=…</c>
/// redirects to the payment platform. The hop records that the buyer opened checkout, and keeps
/// links that an assistant cached working if the merchant later changes payment platform.
/// </summary>
public sealed class TrackedCheckoutLinks(PublicUrls urls) : ICheckoutLinkIssuer
{
    public Uri Issue(Core.Catalog.Offer offer, CheckoutContext context) =>
        new(urls.Base, $"checkout/{Uri.EscapeDataString(offer.Id)}?ref={Uri.EscapeDataString(context.ReferenceId)}&channel={Uri.EscapeDataString(context.Channel)}");
}

/// <summary>Names the assistant a request came from, for attribution. Never trusted for anything else.</summary>
public static partial class Channels
{
    public static string FromClient(string? clientName, string? userAgent)
    {
        var text = $"{clientName} {userAgent}".ToLowerInvariant();
        return text switch
        {
            _ when text.Contains("perplexity") => "perplexity",
            _ when text.Contains("openai") || text.Contains("chatgpt") => "chatgpt",
            _ when text.Contains("claude") || text.Contains("anthropic") => "claude",
            _ when text.Contains("gemini") || text.Contains("google") => "gemini",
            _ => "mcp",
        };
    }

    /// <summary>Who sent a buyer clicking a plain link: browsers send the assistant's origin as Referer.</summary>
    public static string FromReferrer(string? referrer, string ownHost)
    {
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var url))
            return "link";
        var host = url.Host.ToLowerInvariant();
        return host switch
        {
            _ when host.EndsWith("perplexity.ai") => "perplexity",
            _ when host.EndsWith("chatgpt.com") || host.EndsWith("openai.com") => "chatgpt",
            _ when host.EndsWith("claude.ai") => "claude",
            _ when host.EndsWith("gemini.google.com") => "gemini",
            _ when host == ownHost.ToLowerInvariant() => "web",
            _ => "link",
        };
    }

    public static bool IsValid(string? channel) => channel is not null && Pattern().IsMatch(channel);

    public static bool IsLinkPreview(string? userAgent) => userAgent is not null && Preview().IsMatch(userAgent);

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex Pattern();

    [GeneratedRegex("bot|crawl|spider|preview|facebookexternalhit|slack|telegram|whatsapp|discord|skype|embedly|curl|wget|python-requests", RegexOptions.IgnoreCase)]
    private static partial Regex Preview();
}
