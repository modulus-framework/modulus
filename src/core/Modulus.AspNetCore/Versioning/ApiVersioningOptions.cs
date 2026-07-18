namespace Modulus.AspNetCore.Versioning;

/// <summary>
/// Binds from the <c>ApiVersioning</c> configuration section. Backs
/// <see cref="ApiVersioningExtensions.AddModulusApiVersioning"/>.
/// </summary>
public sealed class ModulusApiVersioningOptions
{
    public const string SectionName = "ApiVersioning";

    /// <summary>Default version applied when a request does not specify one. Format: <c>major[.minor]</c>.</summary>
    public string DefaultVersion { get; set; } = "1.0";

    /// <summary>Treat requests with no version as targeting <see cref="DefaultVersion"/>.</summary>
    public bool AssumeDefaultVersionWhenUnspecified { get; set; } = true;

    /// <summary>Emit <c>api-supported-versions</c> / <c>api-deprecated-versions</c> response headers.</summary>
    public bool ReportApiVersions { get; set; } = true;

    /// <summary>Accept the version from the <c>?api-version=</c> query-string parameter.</summary>
    public bool ReadFromQueryString { get; set; } = true;

    /// <summary>Accept the version from a request header (see <see cref="HeaderName"/>).</summary>
    public bool ReadFromHeader { get; set; } = true;

    /// <summary>Accept the version from a URL segment (e.g. <c>/v1/...</c>) when routes declare one.</summary>
    public bool ReadFromUrlSegment { get; set; } = true;

    /// <summary>Header name used when <see cref="ReadFromHeader"/> is enabled.</summary>
    public string HeaderName { get; set; } = "X-Api-Version";

    /// <summary>Query-string parameter name used when <see cref="ReadFromQueryString"/> is enabled.</summary>
    public string QueryStringParameter { get; set; } = "api-version";
}
