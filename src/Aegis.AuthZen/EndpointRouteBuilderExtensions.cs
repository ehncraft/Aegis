using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aegis.AuthZen;

/// <summary>
/// Maps the AuthZEN Authorization API 1.0 evaluation endpoints
/// (https://openid.github.io/authzen/) onto an <see cref="AegisEngine"/>
/// resolved from DI -- register one first, e.g. via <c>Aegis.AspNetCore</c>'s
/// <c>services.AddAegis(...)</c>. Only the evaluation endpoints
/// (<c>/evaluation</c>, <c>/evaluations</c>) are mapped; the spec's search
/// endpoints (subject/resource/action search) need entity enumeration Aegis
/// has no capability for yet, so those routes are intentionally left
/// unmapped (a request to them 404s).
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Requires <c>services.AddAegisHealthChecks()</c> to have been called too.</summary>
    public static IEndpointRouteBuilder MapAegisHealthChecks(this IEndpointRouteBuilder endpoints, string path = "/health")
    {
        endpoints.MapHealthChecks(path);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapAuthZenEndpoints(this IEndpointRouteBuilder endpoints, string prefix = "/access/v1")
    {
        endpoints.MapPost($"{prefix}/evaluation", EvaluateAsync);
        endpoints.MapPost($"{prefix}/evaluations", EvaluateBatchAsync);
        return endpoints;
    }

    private static async Task<IResult> EvaluateAsync(HttpContext http, AegisEngine engine, CancellationToken cancellationToken)
    {
        AuthZenEvaluationRequest request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<AuthZenEvaluationRequest>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Request body is required.");
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        var decision = await EvaluateOneAsync(
            engine, request.Subject, request.Resource, request.Action, request.Context, cancellationToken);
        return Results.Json(ToResponse(decision), JsonOptions);
    }

    private static async Task<IResult> EvaluateBatchAsync(HttpContext http, AegisEngine engine, CancellationToken cancellationToken)
    {
        AuthZenEvaluationsRequest request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<AuthZenEvaluationsRequest>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Request body is required.");
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        // execute_all is the spec's default when options.evaluations_semantic
        // is omitted. deny_on_first_deny/permit_on_first_permit short-circuit
        // and return a partial results array (only the evaluations actually
        // executed), per the spec's own example.
        var semantic = request.Options?.EvaluationsSemantic ?? "execute_all";
        var results = new List<AuthZenEvaluationResponse>();

        foreach (var evaluation in request.Evaluations)
        {
            var subject = evaluation.Subject ?? request.Subject;
            var resource = evaluation.Resource ?? request.Resource;
            var action = evaluation.Action ?? request.Action;
            var context = evaluation.Context ?? request.Context;

            if (subject is null || resource is null || action is null)
            {
                return Results.BadRequest(
                    "Each evaluation must resolve a subject, resource, and action, either per-entry or as a request-level default.");
            }

            var decision = await EvaluateOneAsync(engine, subject, resource, action, context, cancellationToken);
            results.Add(ToResponse(decision));

            if (semantic == "deny_on_first_deny" && !decision.Allowed)
            {
                break;
            }

            if (semantic == "permit_on_first_permit" && decision.Allowed)
            {
                break;
            }
        }

        return Results.Json(new AuthZenEvaluationsResponse { Evaluations = results }, JsonOptions);
    }

    private static Task<AuthorizationDecision> EvaluateOneAsync(
        AegisEngine engine,
        AuthZenEntity subject,
        AuthZenEntity resource,
        AuthZenAction action,
        Dictionary<string, JsonElement>? context,
        CancellationToken cancellationToken) =>
        engine.AuthorizeAsync(
            AuthZenMapping.ToPrincipal(subject),
            AuthZenMapping.ToResource(resource),
            action.Name,
            AuthZenMapping.ToActionProperties(action),
            AuthZenMapping.ToContext(context),
            cancellationToken);

    private static AuthZenEvaluationResponse ToResponse(AuthorizationDecision decision) =>
        new() { Decision = decision.Allowed, Context = decision.Explanation };
}