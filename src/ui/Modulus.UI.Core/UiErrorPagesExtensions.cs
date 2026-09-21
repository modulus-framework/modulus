using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>Model for framework error pages.</summary>
/// <param name="StatusCode">HTTP status code being rendered (4xx/5xx).</param>
/// <param name="Message">Optional detail text; themes supply a default per code.</param>
public sealed record UiErrorViewModel(int StatusCode, string? Message);

/// <summary>
/// Framework error pages for status codes that have no page of their own
/// (403/404/500…). Pair <see cref="UseModulusErrorPages"/> (re-executes failed
/// status codes to <c>/_error/{code}</c>) with <see cref="MapModulusErrorPages"/>
/// (renders the active theme's <c>Errors</c> views via
/// <see cref="IModulusViewResolver"/>, preserving the original status code).
/// </summary>
public static class UiErrorPagesExtensions
{
    private const string ErrorComponent = "Errors";

    /// <summary>Re-executes error status codes to <paramref name="basePath"/>/{code}.</summary>
    public static IApplicationBuilder UseModulusErrorPages(this IApplicationBuilder app, string basePath = "/_error")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        return app.UseStatusCodePagesWithReExecute($"{basePath.TrimEnd('/')}/{{0}}");
    }

    /// <summary>
    /// Maps the error-page endpoints. Each view is resolved through the active
    /// theme; when the theme ships no view for a code, a plain-text fallback
    /// is returned. The view resolves <em>before</em> the status code is
    /// forced, so resolution failures never mask the original status.
    /// </summary>
    public static IEndpointRouteBuilder MapModulusErrorPages(this IEndpointRouteBuilder endpoints, string basePath = "/_error")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var group = endpoints.MapGroup(basePath.TrimEnd('/'));
        group.MapGet("/{code:int}", (HttpContext http, int code, IModulusViewResolver views) =>
        {
            if (!views.TryResolve(ErrorComponent, $"_{code}", out var path))
            {
                return Results.Content($"Error {code}", "text/plain", statusCode: code);
            }

            // The view executor only overrides the status code when the result
            // specifies one, so forcing it here makes the page render as 404/500…
            http.Response.StatusCode = code;
            return (IResult)new ViewPathResult(path, new UiErrorViewModel(code, null));
        });

        return endpoints;
    }

    /// <summary>
    /// Minimal-API result that executes a compiled Razor view by absolute path
    /// (minimal APIs have no built-in view result). Requires MVC view services
    /// (<c>AddRazorPages()</c> / <c>AddControllersWithViews()</c>).
    /// </summary>
    private sealed class ViewPathResult(string viewPath, object model) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var services = httpContext.RequestServices;
            var actionContext = new ActionContext(
                httpContext,
                httpContext.GetRouteData(),
                new ActionDescriptor());

            var viewData = new ViewDataDictionary(
                services.GetRequiredService<IModelMetadataProvider>(),
                new ModelStateDictionary())
            {
                Model = model,
            };

            return new ViewResult { ViewName = viewPath, ViewData = viewData }
                .ExecuteResultAsync(actionContext);
        }
    }
}
