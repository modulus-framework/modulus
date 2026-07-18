namespace Modulus.AspNetCore.OpenApi;

/// <summary>
/// Binds from the <c>OpenApi</c> configuration section. Backs
/// <see cref="OpenApiExtensions.AddModulusOpenApi"/> — document metadata plus a
/// JWT bearer security scheme so generated docs (Scalar/Swagger UI) can authorize.
/// </summary>
public sealed class ModulusOpenApiOptions
{
    public const string SectionName = "OpenApi";

    /// <summary>Logical document name (the <c>{documentName}</c> in <c>/openapi/{documentName}.json</c>).</summary>
    public string DocumentName { get; set; } = "v1";

    /// <summary>API title shown in the document and UIs.</summary>
    public string Title { get; set; } = "API";

    /// <summary>Document version string (independent of route API versioning).</summary>
    public string Version { get; set; } = "v1";

    /// <summary>Optional long-form API description (Markdown supported by most UIs).</summary>
    public string? Description { get; set; }

    /// <summary>Advertise a JWT bearer security scheme and flag secured operations. Defaults to true.</summary>
    public bool IncludeBearerSecurity { get; set; } = true;

    /// <summary>Optional contact name shown in the document info.</summary>
    public string? ContactName { get; set; }

    /// <summary>Optional contact email shown in the document info.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Optional contact URL shown in the document info.</summary>
    public string? ContactUrl { get; set; }

    /// <summary>Optional license name shown in the document info (e.g. <c>MIT</c>).</summary>
    public string? LicenseName { get; set; }

    /// <summary>Optional license URL shown in the document info.</summary>
    public string? LicenseUrl { get; set; }
}
