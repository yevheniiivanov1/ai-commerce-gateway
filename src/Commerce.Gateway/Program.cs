using Commerce.Core;
using Commerce.Core.Catalog;
using Commerce.Core.Checkout;
using Commerce.Core.Enrollment;
using Commerce.Core.Facts;
using Commerce.Core.Scheduling;
using Commerce.Core.Search;
using Commerce.Gateway.Endpoints;
using Commerce.Gateway.Hosting;
using Commerce.Gateway.Mcp;
using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// PaaS hosts (Render, Cloud Run, Heroku) name the port to bind in PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://+:{port}");

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.Section));
builder.Services.PostConfigure<GatewayOptions>(o => o.Normalize());
var options = (builder.Configuration.GetSection(GatewayOptions.Section).Get<GatewayOptions>() ?? new GatewayOptions()).Normalize();

// Checkout platforms. A new platform is one more ICheckoutProvider; offers name theirs in the catalog.
ICheckoutProvider[] checkoutProviders = [new StripePaymentLinkProvider(), new PlainLinkProvider()];
var providerRegistry = new CheckoutProviderRegistry(checkoutProviders);

// The catalog is loaded and validated before the app accepts traffic: bad data fails the deploy.
var catalogPath = Path.IsPathRooted(options.CatalogPath) ? options.CatalogPath : Path.Combine(AppContext.BaseDirectory, options.CatalogPath);
var document = await new JsonFileCatalogSource(catalogPath).LoadAsync(CancellationToken.None);
if (CatalogValidator.Validate(document, providerRegistry.Names) is { Count: > 0 } errors)
    throw new InvalidCatalogException(catalogPath, errors);
var catalog = new Catalog(document);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(providerRegistry);
builder.Services.AddSingleton<IFunnelLog, InMemoryFunnelLog>();
builder.Services.AddSingleton<ReaderLog>();
builder.Services.AddSingleton<ProductSearch>();
builder.Services.AddSingleton<AvailabilityService>();
builder.Services.AddSingleton<ProgramFacts>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PublicUrls>();
builder.Services.AddScoped<ICheckoutLinkIssuer, TrackedCheckoutLinks>();
builder.Services.AddScoped<EnrollmentService>();
builder.Services.AddScoped<Storefront>();

builder.Services
    .AddMcpServer(mcp =>
    {
        mcp.ServerInfo = new()
        {
            Name = $"{catalog.Merchant.Id}-commerce",
            Title = $"{catalog.Merchant.Name} programs & enrollment" + (options.PublicNotice is null ? "" : " (prototype)"),
            Version = "1.0.0",
        };
        mcp.ServerInstructions = ServerInstructions.For(catalog, options.PublicNotice);
    })
    // Stateless: no session affinity, so the gateway scales out behind any load balancer.
    .WithHttpTransport(http => http.Stateless = true)
    .WithTools(CommerceTools.Create(catalog));

builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = $"{catalog.Merchant.Name} storefront API";
    doc.Info.Description = "The MCP tools as REST: search programs, read details, check dates, start enrollment.";
    return Task.CompletedTask;
}));

// Behind a TLS-terminating proxy (Fly, Render, Azure), trust X-Forwarded-* so absolute links are https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseReaderLog();
app.MapOpenApi();
app.MapGet("/openapi.json", () => Results.Redirect("/openapi/v1.json")).ExcludeFromDescription();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", catalog = catalog.Merchant.Id, products = catalog.Document.Products.Count }))
    .ExcludeFromDescription();

app.MapMcp("/mcp");
app.MapCommerceApi();
app.MapCheckout();
app.MapReaderLog();
app.MapDiscovery();

app.Run();

public partial class Program;
