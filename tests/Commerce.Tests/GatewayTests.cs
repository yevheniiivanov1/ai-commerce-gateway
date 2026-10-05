using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Commerce.Core.Checkout;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Commerce.Tests;

/// <summary>The whole path through the real host: MCP client → tools → tracked checkout → Stripe → webhook.</summary>
public class GatewayTests : IClassFixture<GatewayTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeTimeProvider Clock { get; } = TestCatalog.Clock();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Gateway:StripeWebhookSecret", "whsec_test");
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
        }
    }

    private readonly Factory _factory;

    public GatewayTests(Factory factory) => _factory = factory;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<McpClient> ConnectAsync(string clientName)
    {
        var http = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            NullLoggerFactory.Instance,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new() { Name = clientName, Version = "1.0" } }, cancellationToken: Ct);
    }

    private async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?> args)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: Ct);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        Assert.True(result.IsError is not true, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task An_mcp_client_goes_from_a_vague_request_to_the_merchants_checkout()
    {
        await using var client = await ConnectAsync("perplexity-connector");

        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        Assert.Equal(["check_availability", "get_program_details", "search_programs", "start_enrollment"], tools.Select(t => t.Name).Order());
        Assert.Contains("rolling enrollment", client.ServerInstructions);

        // "I want to improve my Double Axel and I'm looking for an online program under $350."
        var search = await CallAsync(client, "search_programs", new() { ["query"] = "improve my Double Axel", ["maxPrice"] = 350 });
        var program = search.GetProperty("programs").EnumerateArray().Single();
        Assert.Equal("vsa-double-axel-club", program.GetProperty("programId").GetString());
        Assert.True(program.GetProperty("withinBudget").GetBoolean());
        Assert.Equal("$299 billed every 6 months until cancelled (auto-renews)", program.GetProperty("price").GetString());

        var details = await CallAsync(client, "get_program_details", new() { ["programId"] = "vsa-double-axel-club", ["timeZone"] = "Europe/London" });
        Assert.Equal("Marta", details.GetProperty("coaches")[0].GetProperty("name").GetString());
        Assert.Equal("Level 3", details.GetProperty("level").GetProperty("label").GetString());
        Assert.Equal("open", details.GetProperty("enrollment").GetProperty("status").GetString());
        Assert.Contains("struck-through", details.GetProperty("pricing").GetProperty("listPriceNote").GetString());

        // "I'm free on November 6. Can I join around that time?"
        var dates = await CallAsync(client, "check_availability", new() { ["programId"] = "vsa-double-axel-club", ["date"] = "2026-11-06" });
        Assert.True(dates.GetProperty("openForEnrollment").GetBoolean());
        Assert.Equal("Friday", dates.GetProperty("requestedDayOfWeek").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-11-07T15:00:00Z"), dates.GetProperty("suggestedFirstClass").GetProperty("startsAtUtc").GetDateTimeOffset());

        // "I want to join."
        var enrollment = await CallAsync(client, "start_enrollment", new() { ["programId"] = "vsa-double-axel-club", ["preferredStartDate"] = "2026-11-06" });
        var reference = enrollment.GetProperty("referenceId").GetString()!;
        var checkoutUrl = new Uri(enrollment.GetProperty("checkoutUrl").GetString()!);
        Assert.Contains(enrollment.GetProperty("disclosures").EnumerateArray(), d => d.GetString()!.Contains("auto-renews"));
        Assert.Equal($"/checkout/vsa-double-axel-club-6m?ref={reference}&channel=perplexity", checkoutUrl.PathAndQuery);

        var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var redirect = await browser.GetAsync(checkoutUrl.PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(
            $"https://buy.stripe.com/14AeVd6anbti69kcJ9dMM1M?client_reference_id={reference}&utm_source=perplexity&utm_medium=ai-assistant&utm_campaign=agentic-enrollment",
            redirect.Headers.Location!.AbsoluteUri);

        // Stripe reports the payment; it is attributed back to the conversation.
        await PostSignedWebhookAsync(browser, JsonSerializer.Serialize(new
        {
            type = "checkout.session.completed",
            data = new { @object = new { client_reference_id = reference, amount_total = 29900, currency = "usd", payment_status = "paid" } },
        }));
        var funnel = await browser.GetFromJsonAsync<JsonElement>("/api/funnel?take=500", Ct);
        var stages = funnel.EnumerateArray()
            .Where(e => e.GetProperty("referenceId").GetString() == reference)
            .Select(e => (Stage: e.GetProperty("stage").GetString(), Channel: e.GetProperty("channel").GetString()))
            .ToList();
        Assert.Equal([("PaymentCompleted", "perplexity"), ("CheckoutOpened", "perplexity"), ("EnrollmentStarted", "perplexity")], stages);
    }

    [Fact]
    public async Task Bad_input_comes_back_as_a_tool_error_the_model_can_fix()
    {
        await using var client = await ConnectAsync("test");

        var result = await client.CallToolAsync("check_availability", new Dictionary<string, object?> { ["programId"] = "double-axle", ["date"] = "someday" }, cancellationToken: Ct);

        Assert.True(result.IsError);
        Assert.Contains("vsa-double-axel-club", result.Content.OfType<TextContentBlock>().Single().Text);
    }

    [Fact]
    public async Task Webhooks_with_a_bad_signature_are_refused()
    {
        var http = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={_factory.Clock.GetUtcNow().ToUnixTimeSeconds()},v1=deadbeef");

        var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rest_mirrors_the_tools_with_proper_status_codes()
    {
        var http = _factory.CreateClient();

        var unknown = await http.GetAsync("/api/programs/quad-axel-club", Ct);
        var badDate = await http.GetAsync("/api/programs/vsa-double-axel-club/availability?date=someday", Ct);
        var enroll = await http.PostAsJsonAsync("/api/enrollments", new { programId = "double-axel-club", channel = "chatgpt-actions" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);
        var body = await enroll.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.EndsWith("&channel=chatgpt-actions", body.GetProperty("checkoutUrl").GetString());
    }

    [Fact]
    public async Task Crawlers_get_a_fact_sheet_with_json_ld_and_an_llms_txt()
    {
        var http = _factory.CreateClient();

        var page = await http.GetStringAsync("/programs/double-axel-club", Ct);
        var markdown = await http.GetStringAsync("/programs/double-axel-club.md", Ct);
        var llms = await http.GetStringAsync("/llms.txt", Ct);
        var missing = await http.GetAsync("/programs/quad-axel-club", Ct);

        Assert.Contains("<script type=\"application/ld+json\">", page);
        Assert.Contains("StrikethroughPrice", page);
        Assert.Contains("$299 billed every 6 months until cancelled (auto-renews)", markdown);
        Assert.Contains("http://localhost/mcp", llms);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task PostSignedWebhookAsync(HttpClient http, string payload)
    {
        var timestamp = _factory.Clock.GetUtcNow().ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={StripeWebhook.Sign(payload, timestamp, "whsec_test")}");
        var response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class DiscoveryTests : IClassFixture<GatewayTests.Factory>
{
    private readonly GatewayTests.Factory _factory;

    public DiscoveryTests(GatewayTests.Factory factory) => _factory = factory;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://www.perplexity.ai/", "perplexity")]
    [InlineData("https://chatgpt.com/", "chatgpt")]
    [InlineData(null, "link")]
    public async Task A_plain_checkout_link_is_attributed_by_its_referrer(string? referrer, string channel)
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/checkout/vsa-double-axel-club-6m");
        if (referrer is not null)
            request.Headers.Referrer = new Uri(referrer);

        var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"utm_source={channel}&", response.Headers.Location!.Query);
    }

    [Fact]
    public async Task A_prototype_host_asks_not_to_be_indexed_and_says_what_it_is()
    {
        var http = _factory.CreateClient();

        var page = await http.GetAsync("/programs/double-axel-club", Ct);
        var full = await http.GetStringAsync("/llms-full.txt", Ct);

        Assert.Equal("noindex", page.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("<meta name=\"robots\" content=\"noindex\">", await page.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Not operated by or affiliated with Victory Skating", full);
        // One fetch carries every program, its checkout link and the billing terms.
        Assert.Contains("# 6-Month Triple Jumps Club", full);
        Assert.Contains("**Checkout:** http://localhost/checkout/vsa-double-axel-club-6m", full);
        Assert.Contains("Non-refundable", full);
    }
}
