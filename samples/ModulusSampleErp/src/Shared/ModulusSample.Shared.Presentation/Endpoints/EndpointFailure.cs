using Microsoft.AspNetCore.Http;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation.Results;

namespace ModulusSample.Shared.Presentation;

/// <summary>
/// Writes a <see cref="Result"/> failure as an RFC 7807 problem response from
/// a REPR endpoint. Call with the endpoint's <c>HttpContext</c> and return when
/// the result is a failure:
/// <code>
/// if (result.IsFailure)
/// {
///     await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
///     return;
/// }
/// </code>
/// </summary>
public static class EndpointFailure
{
    public static Task SendFailureAsync(
        HttpContext httpContext, Result result, CancellationToken ct = default)
    {
        if (result.IsSuccess)
            return Task.CompletedTask;

        var problem = ApiResults.Problem(result);
        return problem.ExecuteAsync(httpContext);
    }
}
