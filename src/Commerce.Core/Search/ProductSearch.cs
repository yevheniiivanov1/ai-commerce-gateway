using System.Text.RegularExpressions;
using Commerce.Core.Catalog;

namespace Commerce.Core.Search;

public sealed record SearchRequest(string? Query, decimal? MaxPrice, string? Currency);

public sealed record ProductMatch(Product Product, Offer Offer, double Score, bool? WithinBudget, IReadOnlyList<string> Reasons);

public sealed record SearchResult(IReadOnlyList<ProductMatch> Matches, bool QueryMatchedNothing);

/// <summary>
/// Deterministic keyword ranking over names, aliases, skills and keywords. A catalog of a few
/// dozen products doesn't need embeddings, and exact phrase hits ("double axel" vs "axel") are
/// what decides the ranking. Brand words are ignored: every product here belongs to the brand.
/// </summary>
public sealed partial class ProductSearch
{
    private readonly Catalog.Catalog _catalog;
    private readonly (Regex Pattern, string Replacement)[] _synonyms;

    public ProductSearch(Catalog.Catalog catalog)
    {
        _catalog = catalog;
        // Shorthand buyers type ("2A", "2-axel") comes from the catalog. Variants are matched on
        // normalised text, so "2-axel" and "2 axel" are the same, and longer variants win.
        _synonyms = catalog.Document.Search.Synonyms
            .SelectMany(s => s.Value.Select(v => (Variant: Clean(v), Canonical: Clean(s.Key))))
            .OrderByDescending(s => s.Variant.Length)
            .Select(s => (new Regex($@"(?<![a-z0-9]){Regex.Escape(s.Variant)}(?![a-z0-9])", RegexOptions.CultureInvariant), s.Canonical))
            .ToArray();
    }

    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "the", "and", "or", "for", "to", "of", "in", "on", "my", "me", "i", "im", "is", "are", "do", "does",
        "have", "has", "any", "anything", "want", "looking", "need", "can", "join", "improve", "help", "with", "under",
        "program", "programs", "training", "train", "online", "class", "classes", "course", "club", "lessons", "months",
        "month", "six", "6", "price", "cost", "much", "how", "what", "which", "offer", "offers", "there", "something",
    ];

    public SearchResult Search(SearchRequest request)
    {
        var brandWords = _catalog.Merchant.BrandNames.Concat(_catalog.Merchant.Aliases)
            .SelectMany(Tokens).ToHashSet();
        var query = Normalize(request.Query ?? "");
        var terms = Tokens(query)
            .Where(t => !StopWords.Contains(t) && !brandWords.Contains(t) && !t.All(char.IsDigit))
            .Distinct()
            .ToList();

        var products = _catalog.Listed.ToList();

        // Words every product shares ("ice" from "off-ice", "jumps") say nothing about which one
        // the skater means. Only meaningful with more than one product to tell apart.
        var vocabularies = products.ToDictionary(p => p.Id, Vocabulary);
        if (products.Count > 1)
            terms.RemoveAll(t => vocabularies.Values.All(v => v.Contains(t)));

        // Longest phrase wins: in "2axel club" → "double axel club", the Axel Club's own
        // "axel club" is only a fragment of the Double Axel Club's name.
        var phraseHits = products.SelectMany(Phrases).Where(p => ContainsPhrase(query, p)).ToHashSet();
        phraseHits.RemoveWhere(p => phraseHits.Any(longer => longer.Length > p.Length && ContainsPhrase(longer, p)));

        var matches = products
            .Select(p => Score(p, vocabularies[p.Id], phraseHits, terms, request))
            .ToList();

        // Keep the clear winners: a "double axel" query shouldn't drag in every club that
        // merely contains the word "double". No terms (e.g. "what does VSA offer?") lists everything.
        var top = matches.Max(m => m.Score);
        var anyHit = terms.Count == 0 || top > 0;
        var cutoff = terms.Count == 0 || !anyHit ? double.MinValue : Math.Max(1, top * 0.4);
        var ranked = matches
            .Where(m => m.Score >= cutoff)
            .OrderByDescending(m => m.WithinBudget != false)
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.Product.Level.Rank)
            .ToList();

        return new SearchResult(ranked, QueryMatchedNothing: !anyHit);
    }

    private ProductMatch Score(Product product, HashSet<string> vocabulary, HashSet<string> phraseHits, List<string> terms, SearchRequest request)
    {
        var reasons = new List<string>();
        double score = 0;

        // Whole-phrase hits on the skill or a keyword dominate single-word overlap, so
        // "double axel" ranks the Double Axel Club above the (single) Axel Club.
        foreach (var phrase in Phrases(product).Where(phraseHits.Contains))
        {
            score += 5;
            reasons.Add($"matches \"{phrase}\"");
        }

        var hits = terms.Where(vocabulary.Contains).ToList();
        score += hits.Count;
        if (hits.Count > 0 && reasons.Count == 0)
            reasons.Add($"mentions {string.Join(", ", hits)}");

        var offer = product.PrimaryOffer;
        bool? withinBudget = null;
        if (request.MaxPrice is { } max)
        {
            var currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.ToUpperInvariant();
            if (currency == offer.Price.Currency)
            {
                withinBudget = offer.Price.Amount <= max;
                reasons.Add(withinBudget.Value
                    ? FormattableString.Invariant($"{offer.Price} for {offer.Term.Months} months is within the {currency} {max:0.##} budget")
                    : FormattableString.Invariant($"{offer.Price} for {offer.Term.Months} months is over the {currency} {max:0.##} budget"));
            }
            else
            {
                reasons.Add($"priced in {offer.Price.Currency}; can't compare with a {currency} budget");
            }
        }

        return new ProductMatch(product, offer, score, withinBudget, reasons);
    }

    private IEnumerable<string> Phrases(Product product) =>
        product.Keywords.Append(product.Skill).Append(product.ShortName).Concat(product.Aliases)
            .Select(Normalize).Where(p => p.Contains(' ')).Distinct();

    private HashSet<string> Vocabulary(Product product) =>
        Tokens(string.Join(' ', product.Keywords.Append(product.Skill).Append(product.Name).Concat(product.Aliases))).ToHashSet();

    private string Normalize(string text)
    {
        var normalized = Clean(text);
        foreach (var (pattern, replacement) in _synonyms)
            normalized = pattern.Replace(normalized, replacement);
        return Spaces().Replace(normalized, " ").Trim();
    }

    private static string Clean(string text) =>
        Spaces().Replace(NonWord().Replace(text.ToLowerInvariant().Replace("’", "'").Replace("'", ""), " "), " ").Trim();

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private IEnumerable<string> Tokens(string text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
