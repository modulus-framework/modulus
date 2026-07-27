namespace Modulus.AspNetCore.OpenApi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a hardened OpenAPI document: metadata bound from the <c>OpenApi</c>
/// section plus a JWT Bearer security scheme and per-operation auth requirements.
/// On .NET 10+ uses built-in OpenApi transformers; on .NET 8 falls back to Swashbuckle.
/// </summary>
public static class OpenApiExtensions
{
    /// <summary>
    /// Binds <see cref="ModulusOpenApiOptions"/> and adds the document with the
    /// Modulus document/operation transformers. Bind from configuration and/or
    /// override via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddModulusOpenApi(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusOpenApiOptions>? configure = null)
    {
        var section = configuration.GetSection(ModulusOpenApiOptions.SectionName);
        services.AddOptions<ModulusOpenApiOptions>().Bind(section);
        if (configure is not null)
            services.Configure(configure);

        var options = section.Get<ModulusOpenApiOptions>() ?? new ModulusOpenApiOptions();
        configure?.Invoke(options);

#if NET10_0_OR_GREATER
        services.AddOpenApi(options.DocumentName, openApi =>
        {
            openApi.AddDocumentTransformer<ModulusOpenApiDocumentTransformer>();
            if (options.IncludeBearerSecurity)
                openApi.AddOperationTransformer<AuthorizeCheckOperationTransformer>();
        });
#else
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc(options.DocumentName, new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = options.Title,
                Version = options.Version,
                Description = options.Description,
                Contact = options.ContactName is not null || options.ContactEmail is not null
                    ? new Microsoft.OpenApi.Models.OpenApiContact
                    {
                        Name = options.ContactName,
                        Email = options.ContactEmail,
                        Url = options.ContactUrl is not null ? new Uri(options.ContactUrl) : null,
                    }
                    : null,
                License = options.LicenseName is not null
                    ? new Microsoft.OpenApi.Models.OpenApiLicense
                    {
                        Name = options.LicenseName,
                        Url = options.LicenseUrl is not null ? new Uri(options.LicenseUrl) : null,
                    }
                    : null,
            });

            if (options.IncludeBearerSecurity)
            {
                swagger.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Description = "Paste a JWT access token. The 'Bearer ' prefix is added automatically.",
                });

                swagger.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer",
                        }
                    }] = [],
                });
            }
        });
#endif

        return services;
    }
}
