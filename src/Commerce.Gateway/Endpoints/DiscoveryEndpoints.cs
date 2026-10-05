using System.Text;
using Commerce.Core;
using Commerce.Core.Catalog;
using Commerce.Core.Enrollment;
using Commerce.Core.Facts;
using Commerce.Core.Publishing;
using Commerce.Gateway.Hosting;
using Microsoft.Extensions.Options;
using static System.Net.WebUtility;

namespace Commerce.Gateway.Endpoints;

/// <summary>
/// Crawlable surfaces for answer engines that read the web rather than call tools: semantic HTML
/// fact sheets with JSON-LD, Markdown twins, llms.txt, sitemap. All generated per request from the
/// catalog, so "next class" dates are always current.
/// </summary>
public static class DiscoveryEndpoints
{
    public static void MapDiscovery(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").ExcludeFromDescription();
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value.AllowIndexing)
                context.HttpContext.Response.Headers["X-Robots-Tag"] = "noindex";
            return await next(context);
        });

        group.MapGet("/", (Catalog catalog, ProgramFacts facts, PublicUrls urls, IOptions<GatewayOptions> options) =>
            Results.Content(IndexPage(catalog, facts, urls.Base, options.Value), "text/html; charset=utf-8"));

        group.MapGet("/programs/{slug}.md", (string slug, Catalog catalog, ProgramFacts facts, PublicUrls urls, IOptions<GatewayOptions> options) =>
            catalog.Find(slug) is { } product
                ? Results.Text(WithNotice(FactSheet.Markdown(facts.Details(product, null, urls.Base)), options.Value), "text/markdown; charset=utf-8")
                : Results.NotFound());

        group.MapGet("/programs/{slug}/jsonld", (string slug, Catalog catalog) =>
            catalog.Find(slug) is { } product
                ? Results.Text(JsonLd.ScriptTag(catalog, product, now: null), "text/plain; charset=utf-8")
                : Results.NotFound());

        group.MapGet("/programs/{slug}", (string slug, Catalog catalog, ProgramFacts facts, TimeProvider clock, PublicUrls urls, IOptions<GatewayOptions> options) =>
            catalog.Find(slug) is { } product
                ? Results.Content(ProgramPage(catalog, product, facts.Details(product, null, urls.Base), clock.GetUtcNow(), options.Value), "text/html; charset=utf-8")
                : Results.NotFound());

        group.MapGet("/llms.txt", (Catalog catalog, ProgramFacts facts, PublicUrls urls, IOptions<GatewayOptions> options) =>
            Results.Text(WithNotice(LlmsIndex(catalog, facts, urls.Base), options.Value), "text/markdown; charset=utf-8"));

        // Everything in one fetch, for a reader that won't follow links.
        group.MapGet("/llms-full.txt", (Catalog catalog, ProgramFacts facts, PublicUrls urls, IOptions<GatewayOptions> options) =>
        {
            var full = new StringBuilder(LlmsIndex(catalog, facts, urls.Base));
            foreach (var product in catalog.Listed.OrderBy(p => p.Level.Rank))
                full.Append("\n---\n\n").Append(FactSheet.Markdown(facts.Details(product, null, urls.Base)));
            return Results.Text(WithNotice(full.ToString(), options.Value), "text/markdown; charset=utf-8");
        });

        group.MapGet("/robots.txt", (PublicUrls urls) =>
            Results.Text($"User-agent: *\nAllow: /\nDisallow: /checkout/\nDisallow: /webhooks/\n\nSitemap: {new Uri(urls.Base, "sitemap.xml")}\n", "text/plain"));

        group.MapGet("/sitemap.xml", (Catalog catalog, PublicUrls urls) =>
        {
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            foreach (var path in catalog.Listed.Select(p => $"programs/{p.Slug}").Prepend("llms-full.txt").Prepend("llms.txt").Prepend(""))
                xml.Append($"  <url><loc>{new Uri(urls.Base, path)}</loc><lastmod>{catalog.Document.UpdatedOn:yyyy-MM-dd}</lastmod></url>\n");
            return Results.Text(xml.Append("</urlset>\n").ToString(), "application/xml");
        });
    }

    private static string LlmsIndex(Catalog catalog, ProgramFacts facts, Uri baseUrl)
    {
        var products = catalog.Listed.OrderBy(p => p.Level.Rank).ToList();
        var cards = products.Select(p => facts.Card(p, baseUrl)).ToList();
        return FactSheet.LlmsTxt(facts, products, cards, baseUrl);
    }

    private static string WithNotice(string markdown, GatewayOptions options) =>
        options.PublicNotice is { } notice ? $"{markdown.TrimEnd()}\n\n_{notice}_\n" : markdown;

    private static string IndexPage(Catalog catalog, ProgramFacts facts, Uri baseUrl, GatewayOptions options)
    {
        var merchant = catalog.Merchant;
        var body = new StringBuilder();
        body.Append($"<p class=\"eyebrow\">AI-readable catalog</p><h1>{HtmlEncode(facts.BrandName)}</h1><p class=\"lead\">{HtmlEncode(merchant.Description)}</p>");
        body.Append("<h2>Programs</h2><ul class=\"cards\">");
        foreach (var product in catalog.Listed.OrderBy(p => p.Level.Rank))
        {
            var card = facts.Card(product, baseUrl);
            body.Append($"<li><a href=\"/programs/{product.Slug}\"><strong>{HtmlEncode(card.Name)}</strong></a>" +
                $"<span>{HtmlEncode(product.Level.Label)} · {HtmlEncode(card.Skill)}</span>" +
                $"<span>{HtmlEncode(card.Price)}</span><span>{HtmlEncode(card.Schedule)}</span><span>Instructors: {HtmlEncode(card.Instructors)}</span></li>");
        }
        body.Append("</ul>");
        body.Append("<h2>For AI assistants and agents</h2><dl class=\"facts\">");
        Row(body, "MCP server", $"<code>{HtmlEncode(new Uri(baseUrl, "mcp").ToString())}</code> — Streamable HTTP, no auth. Tools: search_programs, get_program_details, check_availability, start_enrollment.", raw: true);
        Row(body, "REST + OpenAPI", $"<a href=\"/openapi/v1.json\">/openapi/v1.json</a> · <a href=\"/api/programs\">/api/programs</a>", raw: true);
        Row(body, "LLM index", "<a href=\"/llms.txt\">/llms.txt</a> · everything in one file: <a href=\"/llms-full.txt\">/llms-full.txt</a>", raw: true);
        Row(body, "Funnel", "<a href=\"/api/funnel\">/api/funnel</a> — enrollment → checkout → payment events", raw: true);
        body.Append("</dl>");
        return Page($"{facts.BrandName} — AI-readable catalog", merchant.Description, null, body.ToString(), options);
    }

    private static string ProgramPage(Catalog catalog, Product product, ProgramDetails p, DateTimeOffset now, GatewayOptions options)
    {
        var offer = product.Offers.First(o => o.Id == p.Enrollment.OfferId);
        var body = new StringBuilder();
        body.Append($"<p class=\"eyebrow\"><a href=\"/\">{HtmlEncode(p.Brand.Name)}</a> · AI-readable fact sheet · <a href=\"/programs/{product.Slug}.md\">Markdown</a></p>");
        body.Append($"<h1>{HtmlEncode(p.Name)}</h1><p class=\"lead\">{HtmlEncode(p.Summary)}</p>");

        body.Append("<dl class=\"facts\">");
        Row(body, "Price", p.Pricing.Billing);
        if (p.Pricing.ListPriceNote is { } listNote)
            Row(body, "Former price", listNote);
        Row(body, "Term", $"{p.Pricing.Term} ({p.Pricing.PerClass})");
        Row(body, "Schedule", p.Schedule.Pattern);
        Row(body, "Enrollment", p.Schedule.Enrollment);
        Row(body, "Instructors", string.Join("; ", p.Instructors.Select(c => $"{c.Title} {c.Name} ({c.Teaches})")));
        Row(body, "Level", $"{p.Level.Label} — {p.Level.ForWho}");
        if (p.Level.NotSuitableFor is { } not)
            Row(body, "Not for", not);
        Row(body, "Format", $"{p.Format.Mode}, {p.Format.Setting}. {p.Format.Where}");
        if (p.Pricing.Cancellation is { } cancel)
            Row(body, "Cancellation", cancel);
        if (p.Pricing.Refunds is { } refunds)
            Row(body, "Refunds", refunds);
        if (p.Enrollment.LandingPageDateNote is { } dateNote)
            Row(body, "Start date on landing page", dateNote);
        body.Append("</dl>");

        body.Append("<h2>Next classes</h2><ul>");
        foreach (var line in p.Schedule.NextClasses)
            body.Append($"<li>{HtmlEncode(line)}</li>");
        body.Append("</ul><table><thead><tr><th>Time zone</th><th>Next class starts</th></tr></thead><tbody>");
        foreach (var (zone, time) in p.Schedule.NextClassAroundTheWorld)
            body.Append($"<tr><td>{HtmlEncode(zone)}</td><td>{HtmlEncode(time)}</td></tr>");
        body.Append("</tbody></table>");

        List(body, "What's included", p.Included);
        body.Append($"<h2>Program</h2><p>{HtmlEncode(p.Description)}</p><ul>");
        foreach (var module in p.Curriculum)
            body.Append($"<li><strong>{HtmlEncode(module.Title)}:</strong> {HtmlEncode(string.Join("; ", module.Points))}</li>");
        body.Append($"</ul><p><strong>You need:</strong> {HtmlEncode(string.Join(", ", p.Equipment))}.</p>");

        body.Append("<h2>Instructors</h2><ul>");
        foreach (var instructor in p.Instructors)
            body.Append($"<li><strong>{HtmlEncode(instructor.Title)} {HtmlEncode(instructor.Name)}</strong> ({HtmlEncode(instructor.Teaches)}). {HtmlEncode(instructor.Bio ?? "")} {HtmlEncode(instructor.PrivateLessonRate ?? "")}</li>");
        body.Append("</ul>");

        if (p.FreeTrial is { } trial)
            body.Append($"<h2>Try it free</h2><p><a href=\"{HtmlEncode(trial.Url.ToString())}\">{HtmlEncode(trial.Name)}</a> — {HtmlEncode(trial.Description ?? "")}</p>");
        if (p.MerchantClaims.Count > 0)
            List(body, "Merchant claims", p.MerchantClaims);

        body.Append("<h2>Enroll</h2><ul>");
        foreach (var line in EnrollmentService.Disclosures(offer))
            body.Append($"<li>{HtmlEncode(line)}</li>");
        // No channel parameter: the redirect attributes the click from its Referer (e.g. perplexity.ai).
        body.Append($"</ul><p><a class=\"cta\" href=\"{HtmlEncode(p.Enrollment.CheckoutPage.ToString())}\">Enroll — {HtmlEncode(offer.BillingSummary)}</a></p>");

        body.Append($"<footer>Last verified {p.LastVerified:yyyy-MM-dd} against " +
            string.Join(", ", p.Sources.Select(s => $"<a href=\"{HtmlEncode(s.Url.ToString())}\">{HtmlEncode(s.Url.Host + s.Url.AbsolutePath)}</a>")) +
            $". Contact: {HtmlEncode(p.Brand.ContactEmail ?? "")}.</footer>");

        return Page($"{p.Name} — {p.Brand.Name}", p.Summary, JsonLd.ScriptTag(catalog, product, now) +
            $"<link rel=\"alternate\" type=\"text/markdown\" href=\"/programs/{product.Slug}.md\">", body.ToString(), options);
    }

    private static void Row(StringBuilder body, string label, string value, bool raw = false) =>
        body.Append($"<dt>{HtmlEncode(label)}</dt><dd>{(raw ? value : HtmlEncode(value))}</dd>");

    private static void List(StringBuilder body, string title, IEnumerable<string> items)
    {
        body.Append($"<h2>{HtmlEncode(title)}</h2><ul>");
        foreach (var item in items)
            body.Append($"<li>{HtmlEncode(item)}</li>");
        body.Append("</ul>");
    }

    private static string Page(string title, string description, string? head, string body, GatewayOptions options) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{HtmlEncode(title)}}</title>
        <meta name="description" content="{{HtmlEncode(description)}}">
        {{(options.AllowIndexing ? "" : "<meta name=\"robots\" content=\"noindex\">")}}
        {{head}}
        <style>
        :root { --bg:#fbfaf8; --fg:#1d1f23; --muted:#5d636e; --line:#e3e0da; --accent:#0b5cad; --card:#fff; }
        @media (prefers-color-scheme: dark) { :root { --bg:#16181c; --fg:#e8e6e3; --muted:#a2a8b3; --line:#2c3038; --accent:#7ab4ff; --card:#1d2026; } }
        * { box-sizing:border-box; }
        body { margin:0; background:var(--bg); color:var(--fg); font:16px/1.55 system-ui, -apple-system, "Segoe UI", sans-serif; }
        main { max-width:860px; margin:0 auto; padding:32px 16px 64px; }
        a { color:var(--accent); }
        h1 { font-size:2rem; line-height:1.2; margin:.2em 0 .3em; }
        h2 { font-size:1.15rem; margin:2em 0 .6em; padding-bottom:.3em; border-bottom:1px solid var(--line); }
        .eyebrow { color:var(--muted); font-size:.85rem; margin:0; }
        .lead { font-size:1.1rem; color:var(--muted); }
        dl.facts { display:grid; grid-template-columns:minmax(120px, 200px) 1fr; gap:8px 20px; margin:24px 0; padding:20px; background:var(--card); border:1px solid var(--line); border-radius:10px; }
        dl.facts dt { font-weight:600; } dl.facts dd { margin:0; }
        table { border-collapse:collapse; width:100%; font-size:.92rem; } td, th { text-align:left; padding:6px 8px; border-bottom:1px solid var(--line); }
        ul.cards { list-style:none; padding:0; display:grid; gap:12px; } ul.cards li { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:14px 16px; display:grid; gap:2px; } ul.cards span { color:var(--muted); font-size:.92rem; }
        .cta { display:inline-block; background:var(--accent); color:var(--bg); padding:10px 18px; border-radius:8px; text-decoration:none; font-weight:600; }
        code { font-size:.9em; overflow-wrap:anywhere; }
        footer { margin-top:48px; color:var(--muted); font-size:.85rem; }
        .notice { font-size:.85rem; color:var(--muted); border:1px dashed var(--line); border-radius:8px; padding:8px 12px; margin:0 0 20px; }
        @media (max-width:560px) { dl.facts { grid-template-columns:1fr; } h1 { font-size:1.6rem; } }
        </style>
        </head>
        <body><main>
        {{(options.PublicNotice is { } notice ? $"<p class=\"notice\">{HtmlEncode(notice)}</p>" : "")}}
        {{body}}
        </main></body>
        </html>
        """;
}
