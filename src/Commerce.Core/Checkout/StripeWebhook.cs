using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Commerce.Core.Checkout;

public sealed record CompletedCheckout(string? ReferenceId, long? AmountTotalMinor, string? Currency, string? PaymentStatus)
{
    public bool IsPaid => PaymentStatus is "paid" or "no_payment_required";

    /// <summary>Stripe amounts are in the currency's smallest unit; a few currencies have no decimals.</summary>
    public decimal? AmountTotal => AmountTotalMinor is { } minor && Currency is { } currency
        ? ZeroDecimalCurrencies.Contains(currency) ? minor : minor / 100m
        : null;

    // https://docs.stripe.com/currencies#zero-decimal
    private static readonly HashSet<string> ZeroDecimalCurrencies =
        ["BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"];
}

/// <summary>
/// Verifies and reads Stripe's <c>checkout.session.completed</c> webhook — the moment an
/// AI-started enrollment becomes a sale. Signature scheme per https://docs.stripe.com/webhooks.
/// </summary>
public static class StripeWebhook
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static bool VerifySignature(string payload, string? signatureHeader, string secret, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(secret))
            return false;

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
                continue;
            if (kv[0] == "t" && long.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var t))
                timestamp = t;
            else if (kv[0] == "v1")
                signatures.Add(kv[1]);
        }
        if (timestamp is null || signatures.Count == 0)
            return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value)).Duration() > tolerance)
            return false;

        var expected = Sign(payload, timestamp.Value, secret);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(s), Encoding.ASCII.GetBytes(expected)));
    }

    public static string Sign(string payload, long timestamp, string secret)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the finished session for <c>checkout.session.completed</c> and, for delayed payment
    /// methods (bank debits), <c>checkout.session.async_payment_succeeded</c>; null for anything
    /// else. Only a session whose <see cref="CompletedCheckout.IsPaid"/> is true is money received.
    /// </summary>
    public static CompletedCheckout? ReadCompletedCheckout(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        if (root.GetProperty("type").GetString() is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded"))
            return null;

        var session = root.GetProperty("data").GetProperty("object");
        return new CompletedCheckout(
            String(session, "client_reference_id"),
            session.TryGetProperty("amount_total", out var amount) && amount.ValueKind == JsonValueKind.Number ? amount.GetInt64() : null,
            String(session, "currency")?.ToUpperInvariant(),
            String(session, "payment_status"));
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
