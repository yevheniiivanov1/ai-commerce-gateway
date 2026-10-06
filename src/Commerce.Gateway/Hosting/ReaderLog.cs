using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Commerce.Gateway.Hosting;

/// <summary>Who read the AI-facing surfaces: when, which path, which client. No IPs, no cookies.</summary>
public sealed record ReaderHit(DateTimeOffset At, string Path, int Status, string? UserAgent);

/// <summary>
/// Shows which assistants and crawlers actually fetch llms.txt, the fact sheets and /mcp — the
/// evidence for "how the AI receives the product information". In memory, last 500 hits.
/// </summary>
public sealed class ReaderLog
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<ReaderHit> _hits = new();

    public void Record(ReaderHit hit)
    {
        _hits.Enqueue(hit);
        while (_hits.Count > Capacity)
            _hits.TryDequeue(out _);
    }

    public IReadOnlyList<ReaderHit> Recent(int max) => _hits.Reverse().Take(max).ToList();

    public static bool IsAiSurface(PathString path) =>
        path == "/" || path.StartsWithSegments("/llms.txt") || path.StartsWithSegments("/llms-full.txt")
        || path.StartsWithSegments("/programs") || path.StartsWithSegments("/mcp") || path.StartsWithSegments("/api/programs");
}

public static class ReaderLogEndpoints
{
    public static IApplicationBuilder UseReaderLog(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        await next(context);
        if (ReaderLog.IsAiSurface(context.Request.Path))
        {
            context.RequestServices.GetRequiredService<ReaderLog>().Record(new ReaderHit(
                context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
                context.Request.Path + context.Request.QueryString,
                context.Response.StatusCode,
                context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 200)] : null));
        }
    });

    public static void MapReaderLog(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/readers", (int? take, ReaderLog log, IOptions<GatewayOptions> options) =>
                options.Value.ExposeFunnel ? Results.Ok(log.Recent(Math.Clamp(take ?? 100, 1, 500))) : Results.NotFound())
            .WithTags("Checkout")
            .WithSummary("Recent reads of the AI-facing surfaces (path, client, time — no personal data)");
}
