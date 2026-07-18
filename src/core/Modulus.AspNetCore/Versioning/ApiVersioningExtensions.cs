namespace Modulus.AspNetCore.Versioning;

using System.Globalization;
using Asp.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Wires <c>Asp.Versioning</c> for both minimal-API (REPR) endpoints and the
/// framework's MVC controllers. Configuration lives under the
/// <c>ApiVersioning</c> section (see <see cref="ModulusApiVersioningOptions"/>).
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Registers API versioning with the version readers selected in
    /// configuration and the API-explorer integration used by OpenAPI.
    /// </summary>
    public static IServiceCollection AddModulusApiVersioning(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusApiVersioningOptions>? configure = null)
    {
        var options = configuration
            .GetSection(ModulusApiVersioningOptions.SectionName)
            .Get<ModulusApiVersioningOptions>() ?? new ModulusApiVersioningOptions();
        configure?.Invoke(options);

        services.AddApiVersioning(v =>
            {
                v.DefaultApiVersion = ParseVersion(options.DefaultVersion);
                v.AssumeDefaultVersionWhenUnspecified = options.AssumeDefaultVersionWhenUnspecified;
                v.ReportApiVersions = options.ReportApiVersions;
                v.ApiVersionReader = BuildReader(options);
            })
            .AddApiExplorer(x =>
            {
                // "'v'major[.minor]" — the group name OpenAPI/Swagger uses per version.
                x.GroupNameFormat = "'v'VVV";
                x.SubstituteApiVersionInUrl = options.ReadFromUrlSegment;
            });

        return services;
    }

    private static ApiVersion ParseVersion(string value)
    {
        // Accept "1", "1.0", "2.1" — fall back to 1.0 rather than throwing at boot
        // on a malformed config value the operator can fix without a redeploy loop.
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return new ApiVersion(d);

        var parts = value.Split('.', 2);
        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
        {
            var minor = parts.Length == 2
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
                ? m : 0;
            return new ApiVersion(major, minor);
        }

        return new ApiVersion(1, 0);
    }

    private static IApiVersionReader BuildReader(ModulusApiVersioningOptions options)
    {
        var readers = new List<IApiVersionReader>();
        if (options.ReadFromQueryString)
            readers.Add(new QueryStringApiVersionReader(options.QueryStringParameter));
        if (options.ReadFromHeader)
            readers.Add(new HeaderApiVersionReader(options.HeaderName));
        if (options.ReadFromUrlSegment)
            readers.Add(new UrlSegmentApiVersionReader());

        // Nothing selected → default to query string so a version can still be sent.
        return readers.Count == 0
            ? new QueryStringApiVersionReader(options.QueryStringParameter)
            : ApiVersionReader.Combine([.. readers]);
    }
}
