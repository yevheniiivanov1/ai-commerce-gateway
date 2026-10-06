#:package ModelContextProtocol
#:property PublishAot=false

// Walks the brief's sales dialogue through the gateway's MCP tools, the way an assistant with a
// connector would, then opens the checkout link and reads the funnel. Writes a Markdown transcript.
//
//   dotnet run tools/mcp-demo.cs -- https://vsa-ai-gateway.onrender.com docs/after/mcp-transcript.md

using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var baseUrl = new Uri((args.Length > 0 ? args[0] : "https://vsa-ai-gateway.onrender.com").TrimEnd('/') + "/");
var output = args.Length > 1 ? args[1] : null;
var md = new StringBuilder($"# MCP transcript\n\nServer: `{new Uri(baseUrl, "mcp")}` · run {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · client: official C# MCP SDK\n\n");

await using var client = await McpClient.CreateAsync(
    new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(baseUrl, "mcp"), TransportMode = HttpTransportMode.StreamableHttp }),
    new McpClientOptions { ClientInfo = new() { Name = "mcp-demo", Version = "1.0" } });

var tools = await client.ListToolsAsync();
md.Append($"Tools offered: {string.Join(", ", tools.Select(t => $"`{t.Name}`"))}\n\n");

const string program = "vsa-double-axel-club";
var search = await Step("I want to improve my Double Axel and I'm looking for an online program under $350.",
    "search_programs", new() { ["query"] = "improve my Double Axel", ["maxPrice"] = 350 },
    r => r.GetProperty("programs").EnumerateArray().Select(p =>
        $"{p.GetProperty("name")} — {p.GetProperty("price")}; {p.GetProperty("schedule")}; instructors: {p.GetProperty("instructors")}; within budget: {p.GetProperty("withinBudget")}"));

await Step("How much is it and what is included?",
    "get_program_details", new() { ["programId"] = program, ["timeZone"] = "America/New_York" },
    r => [
        $"Price: {r.GetProperty("pricing").GetProperty("billing")}",
        $"Former price: {r.GetProperty("pricing").GetProperty("listPriceNote")}",
        $"Level: {r.GetProperty("level").GetProperty("label")} — {r.GetProperty("level").GetProperty("forWho")}",
        $"Coach: {r.GetProperty("instructors")[0].GetProperty("title")} {r.GetProperty("instructors")[0].GetProperty("name")}",
        $"Included: {string.Join("; ", r.GetProperty("included").EnumerateArray().Select(i => i.GetString()))}",
        $"Cancellation: {r.GetProperty("pricing").GetProperty("cancellation")} Refunds: {r.GetProperty("pricing").GetProperty("refunds")}",
    ]);

await Step("I'm free on November 6. Can I join the program around that time?",
    "check_availability", new() { ["programId"] = program, ["date"] = "2026-11-06", ["timeZone"] = "America/New_York" },
    r => [$"Answer: {r.GetProperty("answer")}"]);

var enrollment = await Step("I want to join.",
    "start_enrollment", new() { ["programId"] = program, ["preferredStartDate"] = "2026-11-06", ["timeZone"] = "America/New_York" },
    r => [
        $"checkoutUrl: {r.GetProperty("checkoutUrl")}",
        $"Eligibility: {r.GetProperty("eligibility")}",
        .. r.GetProperty("disclosures").EnumerateArray().Select(d => $"Disclosure: {d}"),
        $"First class: {r.GetProperty("firstClass").GetProperty("local")}",
    ]);

// The user clicks the link: the gateway records it and hands over to the merchant's Stripe page.
using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
browser.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130 Safari/537.36");
var redirect = await browser.GetAsync(enrollment.GetProperty("checkoutUrl").GetString());
md.Append($"## The user opens the link\n\n`GET {enrollment.GetProperty("checkoutUrl")}` → **{(int)redirect.StatusCode}** → `{redirect.Headers.Location}`\n\n");

var reference = enrollment.GetProperty("referenceId").GetString();
var funnel = JsonDocument.Parse(await browser.GetStringAsync(new Uri(baseUrl, "api/funnel?take=200"))).RootElement;
md.Append($"## Funnel for `{reference}`\n\n");
foreach (var e in funnel.EnumerateArray().Where(e => e.GetProperty("referenceId").GetString() == reference).Reverse())
    md.Append($"- {e.GetProperty("at")}: **{e.GetProperty("stage")}** · channel `{e.GetProperty("channel")}` · {e.GetProperty("productId")}\n");
md.Append("\n`PaymentCompleted` follows when Stripe's webhook reports the payment; no payment was made in this run.\n");

Console.WriteLine(md);
if (output is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, md.ToString());
}

async Task<JsonElement> Step(string user, string tool, Dictionary<string, object?> arguments, Func<JsonElement, IEnumerable<string>> summary)
{
    var result = await client.CallToolAsync(tool, arguments);
    var json = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();
    md.Append($"## User: \"{user}\"\n\n`{tool}({JsonSerializer.Serialize(arguments)})`\n\n");
    foreach (var line in summary(json))
        md.Append($"- {line}\n");
    md.Append('\n');
    return json;
}
