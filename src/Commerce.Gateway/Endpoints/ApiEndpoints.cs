using System.ComponentModel;
using Commerce.Core;
using Commerce.Core.Facts;
using Commerce.Gateway.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Commerce.Gateway.Endpoints;

public sealed record EnrollmentBody(
    [property: Description("Program to join, e.g. vsa-double-axel-club.")] string ProgramId,
    [property: Description("Offer to buy; defaults to the program's open offer.")] string? OfferId = null,
    [property: Description("Preferred start date, YYYY-MM-DD.")] string? PreferredStartDate = null,
    [property: Description("Buyer's IANA time zone.")] string? TimeZone = null,
    [property: Description("Calling assistant or surface, for attribution, e.g. \"chatgpt-actions\".")] string? Channel = null);

/// <summary>The MCP operations as plain REST + OpenAPI, for agent platforms that speak HTTP actions.</summary>
public static class ApiEndpoints
{
    public static void MapCommerceApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Storefront");

        api.MapGet("/programs", (string? query, decimal? maxPrice, string? currency, Storefront store, PublicUrls urls) =>
                Handle(() => TypedResults.Ok(store.Search(query, maxPrice, currency, urls.Base))))
            .WithName("searchPrograms")
            .WithSummary("Find programs by goal, skill or budget");

        api.MapGet("/programs/{programId}", (string programId, string? timeZone, Storefront store, PublicUrls urls) =>
                Handle(() => TypedResults.Ok(store.Details(programId, timeZone, urls.Base))))
            .WithName("getProgramDetails")
            .WithSummary("Full facts about one program");

        api.MapGet("/programs/{programId}/availability", (string programId, string? date, string? timeZone, Storefront store) =>
                Handle(() => TypedResults.Ok(store.Availability(programId, date, timeZone))))
            .WithName("checkAvailability")
            .WithSummary("Classes and enrollment status around a date");

        api.MapPost("/enrollments", (EnrollmentBody body, Storefront store) =>
                Handle(() => TypedResults.Ok(store.Enroll(
                    body.ProgramId, body.OfferId, body.PreferredStartDate, body.TimeZone,
                    Channels.IsValid(body.Channel) ? body.Channel! : "api"))))
            .WithName("startEnrollment")
            .WithSummary("Start enrollment and get the checkout link");
    }

    private static Results<Ok<T>, NotFound<ProblemDetailsBody>, BadRequest<ProblemDetailsBody>> Handle<T>(Func<Ok<T>> action)
    {
        try
        {
            return action();
        }
        catch (StorefrontException e) when (e.NotFound)
        {
            return TypedResults.NotFound(new ProblemDetailsBody(e.Message));
        }
        catch (StorefrontException e)
        {
            return TypedResults.BadRequest(new ProblemDetailsBody(e.Message));
        }
    }
}

public sealed record ProblemDetailsBody(string Detail);
