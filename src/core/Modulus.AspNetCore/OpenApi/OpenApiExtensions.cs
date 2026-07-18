namespace Modulus.AspNetCore.OpenApi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a hardened OpenAPI document: metadata bound from the <c>OpenApi</c>
/// section plus a JWT Bearer security scheme and per-operation auth requirements.
/// Replaces a bare <c>AddOpenApi()</c>; continue to expose it with
/// <c>app.MapOpenApi()</c>.
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

        services.AddOpenApi(options.DocumentName, openApi =>
        {
            openApi.AddDocumentTransformer<ModulusOpenApiDocumentTransformer>();
            if (options.IncludeBearerSecurity)
                openApi.AddOperationTransformer<AuthorizeCheckOperationTransformer>();
        });

        return services;
    }
}
