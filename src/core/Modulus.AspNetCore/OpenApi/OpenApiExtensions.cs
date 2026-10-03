namespace Modulus.AspNetCore.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a hardened OpenAPI document: metadata bound from the <c>OpenApi</c>
/// section plus a JWT Bearer security scheme and per-operation auth requirements,
/// using .NET's built-in OpenAPI transformers.
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
        AddDocument(services, options.DocumentName, options.IncludeBearerSecurity, configureDocument: null);

        return services;
    }

    /// <summary>
    /// Adds one more OpenAPI document (served at <c>/openapi/{documentName}.json</c>) with the
    /// Modulus info/security transformers, e.g. one document per BFF client. Selects the
    /// document's operations with <paramref name="configureDocument"/>
    /// (<see cref="OpenApiOptions.ShouldInclude"/>).
    /// </summary>
    public static IServiceCollection AddModulusOpenApiDocument(
        this IServiceCollection services,
        IConfiguration configuration,
        string documentName,
        Action<OpenApiOptions>? configureDocument = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(documentName);

        var section = configuration.GetSection(ModulusOpenApiOptions.SectionName);
        services.AddOptions<ModulusOpenApiOptions>().Bind(section);
        var options = section.Get<ModulusOpenApiOptions>() ?? new ModulusOpenApiOptions();
        AddDocument(services, documentName, options.IncludeBearerSecurity, configureDocument);
        return services;
    }

    private static void AddDocument(IServiceCollection services, string documentName, bool includeBearer, Action<OpenApiOptions>? configureDocument)
        => services.AddOpenApi(documentName, openApi =>
        {
            openApi.AddDocumentTransformer<ModulusOpenApiDocumentTransformer>();
            if (includeBearer)
                openApi.AddOperationTransformer<AuthorizeCheckOperationTransformer>();
            configureDocument?.Invoke(openApi);
        });
}
