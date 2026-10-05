using Commerce.Core.Checkout;

namespace Commerce.Tests;

public class CheckoutTests
{
    private readonly Core.Catalog.Catalog _catalog = TestCatalog.Load();

    [Fact]
    public void Stripe_payment_links_carry_the_enrollment_reference_and_channel()
    {
        var offer = _catalog.FindOffer("vsa-double-axel-club-6m")!.Value.Offer;

        var url = TestCatalog.Providers().CreateCheckoutUrl(offer, new CheckoutContext("enr_0123abcd", "perplexity"));

        Assert.Equal(
            "https://buy.stripe.com/14AeVd6anbti69kcJ9dMM1M?client_reference_id=enr_0123abcd&utm_source=perplexity&utm_medium=ai-assistant&utm_campaign=agentic-enrollment",
            url.AbsoluteUri);
    }

    [Theory]
    [InlineData("perplexity", "ai-assistant")]
    [InlineData("web", "referral")]
    [InlineData("link", "referral")]
    public void Only_assistant_traffic_is_tagged_as_coming_from_an_assistant(string channel, string medium)
    {
        var offer = _catalog.FindOffer("vsa-double-axel-club-6m")!.Value.Offer;

        var url = TestCatalog.Providers().CreateCheckoutUrl(offer, new CheckoutContext("enr_1", channel));

        Assert.Contains($"utm_medium={medium}&", url.Query);
    }

    [Theory]
    [InlineData(29900, "USD", 299)]
    [InlineData(45000, "JPY", 45000)]
    public void Webhook_amounts_respect_zero_decimal_currencies(long minor, string currency, decimal expected)
    {
        Assert.Equal(expected, new CompletedCheckout("enr_1", minor, currency, "paid").AmountTotal);
    }

    [Fact]
    public void Existing_query_strings_are_preserved()
    {
        var offer = _catalog.FindOffer("vsa-double-axel-club-6m")!.Value.Offer with
        {
            Checkout = new Core.Catalog.CheckoutTarget("link", new Uri("https://shop.example.com/cart/123:1?locale=en")),
        };

        var url = TestCatalog.Providers().CreateCheckoutUrl(offer, new CheckoutContext("enr_1", "chatgpt"));

        Assert.Equal("https://shop.example.com/cart/123:1?locale=en&utm_source=chatgpt&utm_medium=ai-assistant", url.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public void References_follow_stripes_client_reference_id_rules(string reference)
    {
        var offer = _catalog.FindOffer("vsa-double-axel-club-6m")!.Value.Offer;

        Assert.Throws<ArgumentException>(() => TestCatalog.Providers().CreateCheckoutUrl(offer, new CheckoutContext(reference, "mcp")));
    }

    [Fact]
    public void Webhook_signatures_are_verified_and_replays_refused()
    {
        const string secret = "whsec_test";
        var now = TestCatalog.BaselineEvening;
        var payload = """{"type":"checkout.session.completed","data":{"object":{"client_reference_id":"enr_1","amount_total":29900,"currency":"usd"}}}""";
        var timestamp = now.ToUnixTimeSeconds();
        var header = $"t={timestamp},v1={StripeWebhook.Sign(payload, timestamp, secret)}";

        Assert.True(StripeWebhook.VerifySignature(payload, header, secret, now, StripeWebhook.DefaultTolerance));
        Assert.False(StripeWebhook.VerifySignature(payload.Replace("29900", "100"), header, secret, now, StripeWebhook.DefaultTolerance));
        Assert.False(StripeWebhook.VerifySignature(payload, header, "whsec_other", now, StripeWebhook.DefaultTolerance));
        Assert.False(StripeWebhook.VerifySignature(payload, header, secret, now.AddMinutes(10), StripeWebhook.DefaultTolerance));

        var completed = StripeWebhook.ReadCompletedCheckout(payload)!;
        Assert.Equal("enr_1", completed.ReferenceId);
        Assert.Equal(29900, completed.AmountTotalMinor);
        Assert.Equal("USD", completed.Currency);
    }
}
