namespace Modulus.AI.Connector;

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.AI.Connector.Capabilities;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Execution;

/// <summary>Maps the AI connector's wire contract.</summary>
public static class AiConnectorEndpointExtensions
{
    /// <summary>
    /// Maps the wire contract (v1) under <see cref="ModulusAiConnectorOptions.PathPrefix"/>: <c>GET /manifest</c> and
    /// <c>GET /health</c> for the platform itself (API key), <c>GET /extract</c> and <c>GET /changes</c> for ingestion
    /// (API key, run as the indexing identity of the <c>appInstanceId</c> query parameter), and
    /// <c>POST /capabilities/{name}:execute</c>,
    /// <c>/resources:get</c>, <c>/authz/scope</c>, <c>/authz/resources:check</c> and <c>/authz/fields:check</c> as the
    /// envelope's user. Excluded from OpenAPI. With <see cref="ModulusAiConnectorOptions.Enabled"/> off every route
    /// answers <c>404</c>.
    /// </summary>
    public static RouteGroupBuilder MapModulusAiConnector(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ModulusAiConnectorOptions>>().Value;

        var group = endpoints.MapGroup(options.PathPrefix);
        group.ExcludeFromDescription();
        group.AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<ModulusAiConnectorOptions>>().CurrentValue.Enabled
                ? await next(context)
                : Results.NotFound());

        group.MapGet("/manifest", (AiManifestBuilder manifest) => Results.Json(manifest.Manifest, ConnectorJson.Options))
            .RequireAuthorization(AiConnectorDefaults.ServicePolicy);
        group.MapGet("/health", (IOptions<ModulusAiConnectorOptions> o) =>
                Results.Json(new ConnectorHealth("ok", o.Value.ContractVersion), ConnectorJson.Options))
            .RequireAuthorization(AiConnectorDefaults.ServicePolicy);

        // Ingestion reads as the indexing service identity of one app instance (API key only, no user).
        group.MapGet("/extract", (HttpContext http, string? appInstanceId, string? resourceType, string? cursor, int? limit) =>
                AiIndexer.RunAsync(http, appInstanceId, (service, instance) => service.ExtractAsync(http, instance, resourceType, cursor, limit)))
            .RequireAuthorization(AiConnectorDefaults.ServicePolicy);
        group.MapGet("/changes", (HttpContext http, string? appInstanceId, string? since, int? limit) =>
                AiIndexer.RunAsync(http, appInstanceId, (service, instance) => service.ChangesAsync(http, instance, since, limit)))
            .RequireAuthorization(AiConnectorDefaults.ServicePolicy);

        group.MapPost("/capabilities/{name}:execute", async (HttpContext http, string name, AiConnectorService service) =>
            await ReadAsync<CapabilityExecuteRequest>(http, optional: true) is { Error: null } body
                ? await service.ExecuteAsync(http, name, body.Value)
                : BadRequest())
            .RequireAuthorization(AiConnectorDefaults.Policy);
        group.MapPost("/resources:get", async (HttpContext http, AiConnectorService service) =>
            await ReadAsync<ResourceGetRequest>(http, optional: false) is { Error: null, Value: { } body }
                && !string.IsNullOrWhiteSpace(body.ResourceType) && body.ResourceId is not null
                ? await service.GetResourceAsync(http, body)
                : BadRequest())
            .RequireAuthorization(AiConnectorDefaults.Policy);
        group.MapPost("/authz/scope", async (HttpContext http, AiConnectorService service) =>
            await ReadAsync<ScopeRequest>(http, optional: true) is { Error: null }
                ? await service.ScopeAsync(http)
                : BadRequest())
            .RequireAuthorization(AiConnectorDefaults.Policy);
        group.MapPost("/authz/resources:check", async (HttpContext http, AiConnectorService service) =>
            await ReadAsync<ResourcesCheckRequest>(http, optional: false) is { Error: null, Value: { } body }
                ? await service.CheckResourcesAsync(http, body)
                : BadRequest())
            .RequireAuthorization(AiConnectorDefaults.Policy);
        group.MapPost("/authz/fields:check", async (HttpContext http, AiConnectorService service) =>
            await ReadAsync<FieldsCheckRequest>(http, optional: false) is { Error: null, Value: { } body }
                ? await service.CheckFieldsAsync(http, body)
                : BadRequest())
            .RequireAuthorization(AiConnectorDefaults.Policy);

        return group;
    }

    private readonly record struct Body<T>(T? Value, string? Error);

    // Reads the body with the contract's own settings (not the app's JSON options), so the wire shape never depends on
    // how the host configured MVC or minimal APIs.
    private static async Task<Body<T>> ReadAsync<T>(HttpContext http, bool optional)
        where T : class
    {
        if (http.Request.ContentLength == 0 || (http.Request.ContentLength is null && http.Request.ContentType is null))
            return optional ? new(null, null) : new(null, "empty body");
        if (!http.Request.HasJsonContentType())
            return new(null, "not json");

        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(http.Request.Body, ConnectorJson.Options, http.RequestAborted);
            return value is null && !optional ? new(null, "null body") : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, "malformed json");
        }
    }

    private static IResult BadRequest()
        => Results.Json(
            new ConnectorError(ConnectorErrorCodes.InvalidRequest, "The request body is malformed."),
            ConnectorJson.Options,
            statusCode: StatusCodes.Status400BadRequest);
}
