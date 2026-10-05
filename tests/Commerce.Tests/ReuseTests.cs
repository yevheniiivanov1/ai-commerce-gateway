using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Commerce.Tests;

/// <summary>
/// Proves rather than claims that the gateway is reusable: the same build, pointed at a fictional
/// yoga studio's catalog, must not say a word about skating, Victory Skating or weekends.
/// </summary>
public class ReuseTests : IClassFixture<ReuseTests.YogaFactory>
{
    public sealed class YogaFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Gateway:CatalogPath", Path.Combine(AppContext.BaseDirectory, "catalogs", "flow-yoga.json"));
            builder.UseSetting("Gateway:PublicNotice", "");
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(TestCatalog.Clock()));
        }
    }

    private static readonly string[] ForeignWords = ["skat", "axel", "jump", "weekend", "coach", "victory", "vsa", "stripe", "off-ice"];

    private readonly YogaFactory _factory;

    public ReuseTests(YogaFactory factory) => _factory = factory;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Another_merchants_catalog_produces_no_trace_of_the_first_one()
    {
        var http = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, NullLoggerFactory.Instance, ownsHttpClient: true);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: Ct);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        var outputs = new List<string>
        {
            client.ServerInstructions ?? "",
            string.Join("\n", tools.Select(t => $"{t.Title} {t.Description} {t.JsonSchema}")),
            await Call(client, "search_programs", new() { ["query"] = "vinyasa" }),
            await Call(client, "get_program_details", new() { ["programId"] = "flow-morning-vinyasa" }),
            await Call(client, "check_availability", new() { ["programId"] = "flow-morning-vinyasa", ["date"] = "2026-10-10" }),
            await Call(client, "start_enrollment", new() { ["programId"] = "morning-vinyasa" }),
        };
        var browser = _factory.CreateClient();
        foreach (var path in new[] { "/", "/llms-full.txt", "/programs/morning-vinyasa", "/programs/morning-vinyasa/jsonld", "/openapi/v1.json" })
            outputs.Add(await browser.GetStringAsync(path, Ct));

        foreach (var output in outputs)
            foreach (var word in ForeignWords)
                Assert.False(output.Contains(word, StringComparison.OrdinalIgnoreCase), $"\"{word}\" leaked into: {output[..Math.Min(300, output.Length)]}");

        // …and it describes this merchant properly.
        Assert.Contains("Flow Yoga Studio", outputs[0]);
        Assert.Contains("every Tuesday and Thursday", outputs[3]);
        Assert.Contains("October 10, 2026 is a Saturday, so there is no class that day", outputs[4]);
        var redirect = await _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/checkout/flow-morning-vinyasa-10", Ct);
        Assert.StartsWith("https://flow-yoga.example/book/morning-vinyasa?utm_source=link&utm_medium=referral", redirect.Headers.Location!.AbsoluteUri);
    }

    private async Task<string> Call(McpClient client, string tool, Dictionary<string, object?> args)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: Ct);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        Assert.True(result.IsError is not true, text);
        return text;
    }
}
