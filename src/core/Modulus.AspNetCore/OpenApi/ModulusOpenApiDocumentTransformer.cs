#if NET10_0_OR_GREATER
namespace Modulus.AspNetCore.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

/// <summary>
/// Stamps document-level metadata (title / version / description / contact /
/// license) onto the generated OpenAPI document and, when enabled, registers a
/// reusable JWT <b>Bearer</b> security scheme in the components so UIs expose an
/// "Authorize" affordance.
/// </summary>
internal sealed class ModulusOpenApiDocumentTransformer(IOptions<ModulusOpenApiOptions> options)
    : IOpenApiDocumentTransformer
{
    internal const string BearerSchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var o = options.Value;

        document.Info ??= new OpenApiInfo();
        document.Info.Title = o.Title;
        document.Info.Version = o.Version;
        if (!string.IsNullOrWhiteSpace(o.Description))
            document.Info.Description = o.Description;

        if (o.ContactName is not null || o.ContactEmail is not null || o.ContactUrl is not null)
            document.Info.Contact = new OpenApiContact
            {
                Name = o.ContactName,
                Email = o.ContactEmail,
                Url = ParseUri(o.ContactUrl),
            };

        if (o.LicenseName is not null)
            document.Info.License = new OpenApiLicense
            {
                Name = o.LicenseName,
                Url = ParseUri(o.LicenseUrl),
            };

        if (o.IncludeBearerSecurity)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste a JWT access token. The 'Bearer ' prefix is added automatically.",
            };
        }

        return Task.CompletedTask;
    }

    private static Uri? ParseUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
#endif
