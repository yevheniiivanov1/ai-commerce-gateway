namespace Commerce.Core.Catalog;

/// <summary>An immutable, validated snapshot of one merchant's catalog with lookups.</summary>
public sealed class Catalog
{
    private readonly Dictionary<string, Product> _byId;
    private readonly Dictionary<string, Product> _bySlug;
    private readonly Dictionary<string, (Product Product, Offer Offer)> _offers;

    public Catalog(CatalogDocument document)
    {
        Document = document;
        _byId = document.Products.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        _bySlug = document.Products.ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);
        _offers = document.Products
            .SelectMany(p => p.Offers.Select(o => (Product: p, Offer: o)))
            .ToDictionary(x => x.Offer.Id, StringComparer.OrdinalIgnoreCase);
    }

    public CatalogDocument Document { get; }
    public Merchant Merchant => Document.Merchant;

    /// <summary>Products that may be shown to buyers; retired ones stay resolvable by id only.</summary>
    public IEnumerable<Product> Listed => Document.Products.Where(p => p.Status != ProductStatus.Retired);

    /// <summary>Resolves a product by id or slug, forgiving the casing an assistant may use.</summary>
    public Product? Find(string idOrSlug) =>
        _byId.GetValueOrDefault(idOrSlug) ?? _bySlug.GetValueOrDefault(idOrSlug);

    public (Product Product, Offer Offer)? FindOffer(string offerId) =>
        _offers.TryGetValue(offerId, out var hit) ? hit : null;
}
