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

    // TraceIdentifier is attached explicitly so the contract does not depend on
    // whether the host registered IProblemDetailsService (AddProblemDetails).
    private static Dictionary<string, object?> Extensions(HttpContext ctx)
        => new() { ["traceId"] = ctx.TraceIdentifier };
}
