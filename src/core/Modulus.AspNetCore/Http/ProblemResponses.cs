using Microsoft.AspNetCore.Http;

namespace Modulus.AspNetCore.Http;

/// <summary>
/// Writes RFC 7807 problem responses. Every framework error path — the global
/// exception handler, endpoint binding, endpoint validation, and
/// <c>EndpointBase.SendErrorAsync</c> — emits this single wire contract so API
/// clients never have to handle more than one error shape.
/// </summary>
internal static class ProblemResponses
{
    /// <summary>Writes a problem response with the given status and detail.</summary>
    internal static Task WriteAsync(HttpContext ctx, int statusCode, string detail)
        => Results.Problem(
                detail: detail,
                statusCode: statusCode,
                extensions: Extensions(ctx))
            .ExecuteAsync(ctx);

    /// <summary>
    /// Writes a 400 validation-problem response carrying per-property errors,
    /// the same shape minimal APIs and MVC produce for model-state failures.
    /// </summary>
    internal static Task WriteValidationAsync(
        HttpContext ctx,
        IDictionary<string, string[]> errors,
        string title = "One or more validation errors occurred.")
        => Results.ValidationProblem(
                errors,
                title: title,
                extensions: Extensions(ctx))
            .ExecuteAsync(ctx);

    /// <summary>
    /// Writes a problem response carrying the stable machine-readable <paramref name="code"/> (the value gRPC and GraphQL
    /// report too) and optional extra members.
    /// </summary>
    internal static Task WriteAsync(
        HttpContext ctx,
        int statusCode,
        string title,
        string code,
        IDictionary<string, object?>? extra = null)
        => Results.Problem(
                title: title,
                statusCode: statusCode,
                extensions: Extensions(ctx, code, extra))
            .ExecuteAsync(ctx);

    /// <summary>A 400 validation problem with the stable <c>code</c> member.</summary>
    internal static Task WriteValidationAsync(
        HttpContext ctx,
        IDictionary<string, string[]> errors,
        string title,
        string code)
        => Results.ValidationProblem(
                errors,
                title: title,
                extensions: Extensions(ctx, code, null))
            .ExecuteAsync(ctx);

    // The trace id (W3C when an activity is current, else ASP.NET's) is attached explicitly so the contract does not
    // depend on whether the host registered IProblemDetailsService (AddProblemDetails).
    internal static string TraceId(HttpContext ctx)
        => System.Diagnostics.Activity.Current?.Id ?? ctx.TraceIdentifier;

    private static Dictionary<string, object?> Extensions(
        HttpContext ctx, string? code = null, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?> { ["traceId"] = TraceId(ctx) };
        if (code is not null)
            extensions["code"] = code;
        if (extra is not null)
            foreach (var (key, value) in extra)
                extensions[key] = value;
        return extensions;
    }
}
