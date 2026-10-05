using System.Text.Json;

namespace Commerce.Core.Catalog;

/// <summary>
/// Where product data comes from. The prototype reads a merchant-curated JSON file; a Shopify,
/// WooCommerce or Mindbody source would map that platform's API into the same model.
/// </summary>
public interface ICatalogSource
{
    Task<CatalogDocument> LoadAsync(CancellationToken cancellationToken);
}

public sealed class JsonFileCatalogSource(string path) : ICatalogSource
{
    public async Task<CatalogDocument> LoadAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        try
        {
            return await JsonSerializer.DeserializeAsync<CatalogDocument>(stream, CatalogJson.Options, cancellationToken)
                ?? throw new InvalidCatalogException(path, ["the file is empty"]);
        }
        catch (JsonException e)
        {
            throw new InvalidCatalogException(path, [e.Message]);
        }
    }
}

public sealed class InvalidCatalogException(string source, IReadOnlyList<string> errors)
    : Exception($"Catalog '{source}' is invalid:{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
