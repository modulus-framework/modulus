using System.Net.Http.Json;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.UI;

/// <summary>
/// Maps a failed API response onto the exception the page pipeline already understands, so a
/// split (webapp+api) page's form behaves like an in-process one: a validation-failure answer
/// (the ProblemDetails the framework's <c>GlobalExceptionHandler</c> writes, carrying an
/// <c>errors</c> list) becomes a <see cref="ValidationException"/> — which
/// <see cref="HtmxPageModel.HandleAsync"/> files into ModelState and re-renders the form with
/// 422 — and any other failure becomes an <see cref="HttpRequestException"/> carrying the status
/// code. Generated typed clients call this instead of <c>EnsureSuccessStatusCode</c>, which
/// would drop the error body.
/// </summary>
public static class ApiError
{
    /// <summary>The exception to throw for a non-success <paramref name="response"/>.</summary>
    public static async Task<Exception> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);
            if (problem?.Errors is { Count: > 0 })
            {
                return new ValidationException(problem.Errors);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not a ProblemDetails body (an HTML error page, an empty body, …) — report the status code instead.
        }

        return new HttpRequestException(
            $"The API answered {(int)response.StatusCode} ({response.StatusCode}) for {response.RequestMessage?.RequestUri?.ToString() ?? "the request"}.",
            inner: null,
            statusCode: response.StatusCode);
    }

    private sealed record ProblemBody(string? Title, int? Status, IReadOnlyList<string>? Errors);
}
