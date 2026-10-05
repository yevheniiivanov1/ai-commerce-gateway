using System.Text;
using Commerce.Core.Facts;

namespace Commerce.Core.Publishing;

/// <summary>Plain-text renderings for crawlers and LLM readers: per-program Markdown and llms.txt.</summary>
public static class FactSheet
{
    public static string Markdown(ProgramDetails p)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {p.Name} — {p.Brand.Name}");
        md.AppendLine();
        md.AppendLine($"> {p.Summary}");
        md.AppendLine();
        md.AppendLine("## Key facts");
        md.AppendLine();
        md.AppendLine($"- **Brand:** {p.Brand.Name}; also known as {string.Join(", ", p.Brand.AlsoKnownAs)}");
        md.AppendLine($"- **What it is:** {p.Format.Mode}, {p.Format.Setting} training for {p.Skill}");
        md.AppendLine($"- **Price:** {p.Pricing.Billing}");
        if (p.Pricing.ListPriceNote is { } listNote)
            md.AppendLine($"- **Former price:** {listNote}");
        md.AppendLine($"- **Term:** {p.Pricing.Term} ({p.Pricing.PerClass})");
        md.AppendLine($"- **Schedule:** {p.Schedule.Pattern}");
        md.AppendLine($"- **Enrollment:** {p.Schedule.Enrollment}");
        md.AppendLine($"- **Coach:** {string.Join("; ", p.Coaches.Select(c => $"{c.Title} {c.Name} ({c.Teaches})"))}");
        md.AppendLine($"- **Level:** {p.Level.Label} — {p.Level.ForWho}");
        if (p.Level.NotSuitableFor is { } not)
            md.AppendLine($"- **Not for:** {not}");
        md.AppendLine($"- **Cancellation:** {p.Pricing.Cancellation}");
        md.AppendLine($"- **Refunds:** {p.Pricing.Refunds}");
        if (p.Enrollment.LandingPageDateNote is { } dateNote)
            md.AppendLine($"- **About the date on the landing page:** {dateNote}");
        md.AppendLine();
        md.AppendLine("## Next classes");
        md.AppendLine();
        foreach (var line in p.Schedule.NextClasses)
            md.AppendLine($"- {line}");
        md.AppendLine();
        md.AppendLine("Next class in other time zones:");
        md.AppendLine();
        foreach (var (zone, time) in p.Schedule.NextClassAroundTheWorld)
            md.AppendLine($"- {zone}: {time}");
        md.AppendLine();
        md.AppendLine("## What's included");
        md.AppendLine();
        foreach (var item in p.Included)
            md.AppendLine($"- {item}");
        md.AppendLine();
        md.AppendLine("## Program");
        md.AppendLine();
        md.AppendLine(p.Description);
        md.AppendLine();
        foreach (var module in p.Curriculum)
            md.AppendLine($"- **{module.Title}:** {string.Join("; ", module.Points)}");
        md.AppendLine();
        md.AppendLine($"**You need:** {string.Join(", ", p.Equipment)}.");
        md.AppendLine();
        md.AppendLine("## Coaches");
        md.AppendLine();
        foreach (var coach in p.Coaches)
            md.AppendLine($"- **{coach.Title} {coach.Name}** ({coach.Teaches}). {coach.Bio}" + (coach.PrivateLessonRate is { } rate ? $" {rate}." : ""));
        if (p.FreeTrial is { } trial)
        {
            md.AppendLine();
            md.AppendLine("## Try it free");
            md.AppendLine();
            md.AppendLine($"[{trial.Name}]({trial.Url}) — {trial.Description}");
        }
        if (p.MerchantClaims.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Merchant claims");
            md.AppendLine();
            foreach (var claim in p.MerchantClaims)
                md.AppendLine($"- {claim}");
        }
        md.AppendLine();
        md.AppendLine("## How to enroll");
        md.AppendLine();
        md.AppendLine($"Enrollment is {p.Enrollment.Status}. **Checkout:** {p.Enrollment.CheckoutPage} — the merchant's secure checkout ({p.Pricing.Billing}).");
        md.AppendLine();
        md.AppendLine($"Before paying: {p.Pricing.Cancellation} Refunds: {p.Pricing.Refunds}");
        md.AppendLine();
        md.AppendLine($"Landing page: {p.Enrollment.LandingPage}. AI assistants with tools: call start_enrollment (MCP) for a link tied to the conversation.");
        md.AppendLine();
        md.AppendLine($"Contact: {p.Brand.ContactEmail} · {p.Brand.Website}");
        md.AppendLine();
        md.AppendLine($"_Last verified {p.LastVerified:yyyy-MM-dd} against: {string.Join(", ", p.Sources.Select(s => s.Url))}._");
        return md.ToString();
    }

    /// <summary>https://llmstxt.org — an index an LLM can read in one fetch.</summary>
    public static string LlmsTxt(ProgramFacts facts, IReadOnlyList<Catalog.Product> products, IReadOnlyList<ProgramCard> programs, Uri baseUrl)
    {
        var merchant = facts.Merchant;
        var md = new StringBuilder();
        md.AppendLine($"# {facts.BrandName}");
        md.AppendLine();
        md.AppendLine($"> {merchant.Description} This file and the pages it links are generated from a catalog curated from the merchant's own pages and checkout; it supersedes older or deleted pages.");
        md.AppendLine();
        var common = CommonFacts(products).ToList();
        if (common.Count > 0)
        {
            md.AppendLine("Facts that apply to every program:");
            md.AppendLine();
            foreach (var fact in common)
                md.AppendLine($"- {fact}");
            md.AppendLine();
        }
        md.AppendLine("## Programs");
        md.AppendLine();
        foreach (var p in programs)
            md.AppendLine($"- [{p.Name}]({p.FactSheet}.md): {p.Level.Split(" — ")[0]}, {p.Skill}. {p.Price}. {p.Schedule}. Coach: {p.Coaches}.");
        md.AppendLine();
        md.AppendLine("## For AI agents");
        md.AppendLine();
        md.AppendLine($"- [MCP server]({new Uri(baseUrl, "mcp")}): Streamable HTTP, no auth. Tools: search_programs, get_program_details, check_availability, start_enrollment.");
        md.AppendLine($"- [OpenAPI]({new Uri(baseUrl, "openapi.json")}): the same operations as REST.");
        md.AppendLine($"- [Catalog JSON]({new Uri(baseUrl, "api/programs")}): all programs.");
        md.AppendLine();
        md.AppendLine("## Optional");
        md.AppendLine();
        md.AppendLine($"- [Website]({merchant.Website})");
        if (merchant.TermsUrl is { } terms)
            md.AppendLine($"- [Terms of service]({terms})");
        if (merchant.PrivacyUrl is { } privacy)
            md.AppendLine($"- [Privacy policy]({privacy})");
        if (merchant.ContactEmail is { } email)
            md.AppendLine($"- Contact: {email}");
        return md.ToString();
    }

    /// <summary>Statements true for the whole catalog, derived from the data rather than written by hand.</summary>
    private static IEnumerable<string> CommonFacts(IReadOnlyList<Catalog.Product> products)
    {
        var offers = products.SelectMany(p => p.Offers).ToList();
        if (products.All(p => p.Schedule.Enrollment.Mode == Catalog.EnrollmentMode.Rolling))
            yield return "Programs are recurring with rolling enrollment — join any day. A start date printed on a landing page names the next session at the time of editing; it is not a deadline.";
        if (offers.All(o => o.CompareAtPrice is not null))
            yield return "Landing pages show a struck-through former price next to the real price; quote the real price.";
        if (offers.Select(o => (o.Billing.Type, o.Billing.IntervalMonths, o.Billing.AutoRenews)).Distinct().Count() == 1
            && offers[0].Billing is { Type: Catalog.BillingType.Subscription, IntervalMonths: { } months, AutoRenews: true })
            yield return $"Every package is a subscription billed every {months} months until cancelled.";
        if (offers.Select(o => o.Billing.RefundPolicy).Distinct().Count() == 1 && offers[0].Billing.RefundPolicy is { } refunds)
            yield return $"Refund policy: {refunds}";
    }
}
