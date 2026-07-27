using Microsoft.AspNetCore.Http;

namespace Modulus.AspNetCore.Endpoints;

using Modulus.AspNetCore.Http;

/// <summary>
/// REPR (Request-Endpoint-Response) base class.
/// <para>
/// Define only the request DTO, response DTO, and the handler method.
/// The framework discovers the endpoint, registers the route, applies
/// validation, authorization, versioning, and OpenAPI automatically.
/// </para>
/// <example>
/// <code>
/// public sealed class CreateUserEndpoint : Endpoint&lt;CreateUserRequest, CreateUserResponse&gt;
/// {
///     public override void Configure()
///     {
///         Post("/users");
///         Version(1);
///         Permissions("Users.Create");
///     }
///
///     public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
///     {
///         var user = new User(req.Name);
///         // … save …
///         await SendCreatedAsync(new CreateUserResponse { Id = user.Id }, ct);
///     }
/// }
/// </code>
/// </example>
/// </summary>
public abstract class Endpoint<TRequest, TResponse> : EndpointBase, IEndpointHandler<TRequest>
    where TRequest : class, new()
{
    protected Endpoint()
    {
        Config.RequestType = typeof(TRequest);
        Config.ResponseType = typeof(TResponse);
    }

    /// <summary>
    /// Override to implement the endpoint logic.
    /// Send the response using the <c>Send*</c> helper methods.
    /// </summary>
    public abstract Task HandleAsync(TRequest req, CancellationToken ct);

    // ── Response helpers with payload ──────────────────────────────

    protected Task SendOkAsync(TResponse response, CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        var payload = Config.WrapResponse
            ? (object)ApiResponse<TResponse>.Ok(response)
            : response;
        return HttpContext.Response.WriteAsJsonAsync(payload, ct);
    }

    protected Task SendCreatedAsync(TResponse response, string? location = null, CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        if (location is not null)
            HttpContext.Response.Headers.Location = location;

        var payload = Config.WrapResponse
            ? (object)ApiResponse<TResponse>.Ok(response, "Created")
            : response;
        return HttpContext.Response.WriteAsJsonAsync(payload, ct);
    }

    protected Task SendAsync(TResponse response, int statusCode, CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = statusCode;
        return HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

/// <summary>
/// Endpoint variant for requests with no response body (HTTP 204).
/// </summary>
public abstract class Endpoint<TRequest> : EndpointBase, IEndpointHandler<TRequest>
    where TRequest : class, new()
{
    protected Endpoint()
    {
        Config.RequestType = typeof(TRequest);
        Config.ResponseType = null;
    }

    /// <summary>
    /// Override to implement the endpoint logic.
    /// With no response type the endpoint conventionally ends with
    /// <c>SendNoContentAsync</c> or an error helper.
    /// </summary>
    public abstract Task HandleAsync(TRequest req, CancellationToken ct);
}

/// <summary>
/// Endpoint variant for GET-style endpoints with no request body.
/// The handler receives only a cancellation token.
/// </summary>
public abstract class EndpointWithoutRequest<TResponse> : Endpoint<EmptyRequest, TResponse>
{
    protected EndpointWithoutRequest()
    {
        Config.RequestType = typeof(EmptyRequest);
    }

    public sealed override Task HandleAsync(EmptyRequest req, CancellationToken ct)
        => HandleAsync(ct);

    /// <summary>Handler that does not need a request object.</summary>
    protected abstract Task HandleAsync(CancellationToken ct);
}
