using System.Globalization;

namespace Commerce.Core.Catalog;

public sealed record Money(decimal Amount, string Currency)
{
    public override string ToString() => Currency == "USD"
        ? string.Create(CultureInfo.InvariantCulture, $"${Amount:0.##}")
        : string.Create(CultureInfo.InvariantCulture, $"{Amount:0.##} {Currency}");
}

public enum BillingType { OneTime, Subscription }

public sealed record Billing
{
    public required BillingType Type { get; init; }
    public int? IntervalMonths { get; init; }
    public bool AutoRenews { get; init; }
    public string? CancellationPolicy { get; init; }
    public string? RefundPolicy { get; init; }
    /// <summary>Product name as the payment page shows it, so the buyer recognises the checkout.</summary>
    public string? StatementName { get; init; }
}

public sealed record Term(int Months, int Classes);

/// <summary>Where the buyer pays. <see cref="Provider"/> selects an <c>ICheckoutProvider</c>.</summary>
public sealed record CheckoutTarget(string Provider, Uri Url);

public enum OfferAvailability { Open, SoldOut, Paused }

public sealed record Offer
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Money Price { get; init; }
    /// <summary>The struck-through "was" price. Explicit so no reader mistakes it for the price.</summary>
    public Money? CompareAtPrice { get; init; }
    public required Billing Billing { get; init; }
    public required Term Term { get; init; }
    public IReadOnlyList<string> Inclusions { get; init; } = [];
    public required CheckoutTarget Checkout { get; init; }
    public OfferAvailability Availability { get; init; } = OfferAvailability.Open;

    public decimal PricePerClass => Math.Round(Price.Amount / Term.Classes, 2);

    public string BillingSummary => Billing.Type switch
    {
        BillingType.Subscription when Billing.IntervalMonths is { } months =>
            $"{Price} billed every {months} months" + (Billing.AutoRenews ? " until cancelled (auto-renews)" : ""),
        BillingType.Subscription => $"{Price} subscription",
        _ => $"{Price} one-time payment",
    };
}
