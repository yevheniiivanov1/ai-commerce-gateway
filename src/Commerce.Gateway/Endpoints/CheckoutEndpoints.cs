using System.Security.Cryptography;
using Commerce.Core.Catalog;
using Commerce.Core.Checkout;
using Commerce.Core.Enrollment;
using Commerce.Gateway.Hosting;
using Microsoft.Extensions.Options;

namespace Commerce.Gateway.Endpoints;

/// <summary>The transaction end of the funnel: checkout redirect, payment webhook, funnel readout.</summary>
public static class CheckoutEndpoints
{
    public static void MapCheckout(this IEndpointRouteBuilder app)
    {
        app.MapGet("/checkout/{offerId}", OpenCheckout)
            .WithTags("Checkout")
            .WithSummary("Redirects to the merchant's payment page for an offer");

        app.MapPost("/webhooks/stripe", StripeWebhookAsync)
            .ExcludeFromDescription();

        app.MapGet("/api/funnel", (int? take, IFunnelLog funnel, IOptions<GatewayOptions> options) =>
                options.Value.ExposeFunnel ? Results.Ok(funnel.Recent(Math.Clamp(take ?? 50, 1, 500))) : Results.NotFound())
            .WithTags("Checkout")
            .WithSummary("Recent enrollment funnel events (no personal data)");
    }

    private static IResult OpenCheckout(
        string offerId, string? @ref, string? channel, HttpRequest request,
        Catalog catalog, CheckoutProviderRegistry providers, IFunnelLog funnel, TimeProvider clock)
    {
        if (catalog.FindOffer(offerId) is not { } hit)
            return Results.NotFound();
        var (product, offer) = hit;

        // A link shared outside the conversation still works; it just starts a new reference.
        var reference = CheckoutProviderRegistry.IsValidReference(@ref) ? @ref! : "lnk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
        var source = Channels.IsValid(channel) ? channel! : Channels.FromReferrer(request.Headers.Referer, request.Host.Host);

        var target = providers.CreateCheckoutUrl(offer, new CheckoutContext(reference, source));
        funnel.Record(new FunnelEvent(clock.GetUtcNow(), FunnelStage.CheckoutOpened, reference, product.Id, offer.Id, source, offer.Price));
        return Results.Redirect(target.AbsoluteUri);
    }

    private static async Task<IResult> StripeWebhookAsync(
        HttpRequest request, IOptions<GatewayOptions> options, IFunnelLog funnel, TimeProvider clock, ILogger<GatewayOptions> log)
    {
        if (string.IsNullOrEmpty(options.Value.StripeWebhookSecret))
            return Results.Problem("Stripe webhooks are not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        var now = clock.GetUtcNow();
        if (!StripeWebhook.VerifySignature(payload, request.Headers["Stripe-Signature"], options.Value.StripeWebhookSecret, now, StripeWebhook.DefaultTolerance))
            return Results.BadRequest();

        if (StripeWebhook.ReadCompletedCheckout(payload) is { ReferenceId: { } reference } completed)
        {
            var start = funnel.FindStart(reference);
            var amount = completed.AmountTotalMinor is { } minor && completed.Currency is { } currency
                ? new Money(minor / 100m, currency)
                : null;
            funnel.Record(new FunnelEvent(now, FunnelStage.PaymentCompleted, reference, start?.ProductId, start?.OfferId, start?.Channel, amount));
            log.LogInformation("Payment completed for enrollment {Reference} via {Channel}", reference, start?.Channel ?? "unknown");
        }

        return Results.Ok();
    }
}
