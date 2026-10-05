using System.Security.Cryptography;
using Commerce.Core.Catalog;
using Commerce.Core.Checkout;
using Commerce.Core.Scheduling;

namespace Commerce.Core.Enrollment;

public sealed record EnrollmentRequest(
    string ProductId,
    string? OfferId,
    DateOnly? PreferredStartDate,
    TimeZoneInfo? ViewerZone,
    string Channel);

public sealed record Enrollment(
    string ReferenceId,
    Product Product,
    Offer Offer,
    Uri CheckoutUrl,
    Availability Availability,
    IReadOnlyList<string> Disclosures,
    IReadOnlyList<string> NextSteps);

public enum EnrollmentError { UnknownProduct, UnknownOffer, NotOpen }

public sealed record EnrollmentOutcome(Enrollment? Enrollment, EnrollmentError? Error, string? Message);

/// <summary>
/// Hands out the link the buyer pays at. The gateway issues a tracked redirect on its own domain;
/// <see cref="DirectCheckoutLinks"/> links straight to the payment platform.
/// </summary>
public interface ICheckoutLinkIssuer
{
    Uri Issue(Offer offer, CheckoutContext context);
}

public sealed class DirectCheckoutLinks(CheckoutProviderRegistry providers) : ICheckoutLinkIssuer
{
    public Uri Issue(Offer offer, CheckoutContext context) => providers.CreateCheckoutUrl(offer, context);
}

/// <summary>
/// "I want to join" → an enrollment reference and a checkout link. Nothing is charged here: the
/// buyer pays on the merchant's own checkout, and the reference ties that payment back to the
/// conversation that produced it.
/// </summary>
public sealed class EnrollmentService(
    Catalog.Catalog catalog,
    AvailabilityService availability,
    ICheckoutLinkIssuer links,
    IFunnelLog funnel,
    TimeProvider clock)
{
    public EnrollmentOutcome Start(EnrollmentRequest request)
    {
        if (catalog.Find(request.ProductId) is not { } product)
            return Fail(EnrollmentError.UnknownProduct, $"No program with id '{request.ProductId}'. Use search_programs to find one.");

        var offer = request.OfferId is null
            ? product.Offers.FirstOrDefault(o => o.Availability == OfferAvailability.Open)
            : product.Offers.FirstOrDefault(o => string.Equals(o.Id, request.OfferId, StringComparison.OrdinalIgnoreCase));
        if (offer is null)
            return request.OfferId is null
                ? Fail(EnrollmentError.NotOpen, $"{product.Name} has no offer open for enrollment right now.")
                : Fail(EnrollmentError.UnknownOffer, $"{product.Name} has no offer '{request.OfferId}'. Available: {string.Join(", ", product.Offers.Select(o => o.Id))}.");

        var when = availability.Check(product, request.PreferredStartDate, request.ViewerZone);
        if (!when.OpenForEnrollment || offer.Availability != OfferAvailability.Open)
            return Fail(EnrollmentError.NotOpen, when.Explanation);

        var referenceId = NewReferenceId();
        var checkoutUrl = links.Issue(offer, new CheckoutContext(referenceId, request.Channel));
        funnel.Record(new FunnelEvent(clock.GetUtcNow(), FunnelStage.EnrollmentStarted, referenceId, product.Id, offer.Id, request.Channel, offer.Price));

        return new EnrollmentOutcome(
            new Enrollment(referenceId, product, offer, checkoutUrl, when, Disclosures(offer), NextSteps(product, when)),
            null,
            null);
    }

    /// <summary>What the buyer must hear before paying — billing terms the landing page doesn't show.</summary>
    public static IReadOnlyList<string> Disclosures(Offer offer)
    {
        var lines = new List<string> { $"{offer.BillingSummary}." };
        if (offer.Billing.CancellationPolicy is { } cancel)
            lines.Add(cancel);
        if (offer.Billing.RefundPolicy is { } refunds)
            lines.Add($"Refunds: {refunds}");
        lines.Add("Payment is taken on the merchant's secure checkout page, which may show the amount in your local currency.");
        return lines;
    }

    private static IReadOnlyList<string> NextSteps(Product product, Availability when)
    {
        var steps = new List<string> { "Open the checkout link and complete payment on the merchant's page." };
        if (product.Format.AccessDelivery is { } access)
            steps.Add(access);
        if (when.SuggestedFirstClass is { } first)
            steps.Add($"First class: {TimeFormat.Describe(first, when.ViewerZone)}.");
        if (product.Equipment.Count > 0)
            steps.Add($"Have ready: {string.Join(", ", product.Equipment).ToLowerInvariant()}.");
        return steps;
    }

    private static string NewReferenceId() =>
        "enr_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();

    private static EnrollmentOutcome Fail(EnrollmentError error, string message) => new(null, error, message);
}
