using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Modulus.AspNetCore.Endpoints;

using Modulus.AspNetCore.Http;

/// <summary>
/// Shared base for all REPR endpoints providing configuration DSL,
/// per-request context injection, and response helper methods.
/// </summary>
public abstract class EndpointBase : IModulusEndpoint
{
    internal EndpointConfig Config { get; } = new();

    /// <summary>The current HTTP context for this request.</summary>
    protected HttpContext HttpContext { get; private set; } = null!;

    /// <summary>Request-abort token from the HTTP context.</summary>
    protected CancellationToken CancellationToken { get; private set; }

    /// <summary>Scoped service provider for the current request.</summary>
    protected IServiceProvider Services => HttpContext.RequestServices;

    /// <summary>Called once at startup. Override to configure route, verb,
    /// version, permissions, etc.</summary>
    public abstract void Configure();

    internal void Initialize(HttpContext ctx)
    {
        HttpContext = ctx;
        CancellationToken = ctx.RequestAborted;
    }

    // ── HTTP verb DSL ──────────────────────────────────────────────

    protected void Get(string route) => Config.SetMethod("GET", route);
    protected void Post(string route) => Config.SetMethod("POST", route);
    protected void Put(string route) => Config.SetMethod("PUT", route);
    protected void Patch(string route) => Config.SetMethod("PATCH", route);
    protected void Delete(string route) => Config.SetMethod("DELETE", route);
    protected void Options(string route) => Config.SetMethod("OPTIONS", route);
    protected void Head(string route) => Config.SetMethod("HEAD", route);

    // ── Metadata DSL ───────────────────────────────────────────────

    protected void Versions(params int[] versions) => Config.Versions = versions;
    protected void Permissions(params string[] perms) => Config.Permissions = perms;
    protected void Roles(params string[] roles) => Config.Roles = roles;
    protected void Policies(params string[] policies) => Config.Policies = policies;
    protected void AllowAnonymous() => Config.AllowAnonymous = true;
    protected void Tag(string tag) => Config.Tag = tag;
    protected void Summary(string summary) => Config.Summary = summary;
    protected void Deprecated() => Config.Deprecated = true;
    protected void DontWrapResponse() => Config.WrapResponse = false;

    // ── Common response helpers (no payload) ───────────────────────

    protected Task SendNoContentAsync(CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    protected Task SendNotFoundAsync(CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    protected Task SendUnauthorizedAsync(CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected Task SendForbiddenAsync(CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    protected Task SendErrorAsync(
        int statusCode, string message,
        CancellationToken ct = default)
    {
        HttpContext.Response.StatusCode = statusCode;
        return HttpContext.Response.WriteAsJsonAsync(
            ApiResponse.Fail(message, traceId: HttpContext.TraceIdentifier), ct);
    }

    /// <summary>Throws an <see cref="HttpResponseException"/> to short-circuit
    /// the pipeline with the given status code and message.</summary>
    protected static HttpResponseException ThrowError(
        int statusCode, string message) => new(statusCode, message);
}

/// <summary>
/// Exception that short-circuits the endpoint pipeline.
/// Caught by the endpoint executor and converted to a standard error response.
/// </summary>
public sealed class HttpResponseException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Placeholder for endpoints with no request body.</summary>
public sealed class EmptyRequest { }
