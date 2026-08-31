using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace TradeFlow.Api.Extensions;

/// <summary>
/// Extension methods for configuring Swagger (OpenAPI) documentation.
/// </summary>
public static class SwaggerExtensions
{
    private const string ApiTitle = "TradeFlow API";
    private const string ApiVersion = "v1";

    /// <summary>
    /// Adds Swagger (OpenAPI) documentation configuration.
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        IConfigurationSection swaggerConfig = configuration.GetSection("Swagger");
        bool swaggerEnabled = swaggerConfig.GetValue("Enabled", true);

        if (!swaggerEnabled)
        {
            return services;
        }

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            // Custom schema ID selector to handle duplicate type names across modules
            // Use full type name including namespace to ensure uniqueness
            options.CustomSchemaIds(type => type.FullName ?? type.Name);

            options.SwaggerDoc(ApiVersion, new OpenApiInfo
            {
                Title = ApiTitle,
                Version = ApiVersion,
                Description = """
                    TradeFlow API with modular architecture.

                    ## Authentication
                    API uses JWT bearer tokens for authentication. Include your token in the Authorization header:
                    `Bearer <your-token>`
                    """,
                Contact = new OpenApiContact
                {
                    Name = "TradeFlow API Support",
                    Email = "support@TradeFlow.com",
                    Url = new Uri("https://TradeFlow.com")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Include XML comments if available
            string xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Add security definition for JWT Bearer authentication
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = """
                    JWT Authorization header using the Bearer scheme.

                    Enter 'Bearer' [space] and then your token.

                    Example: `Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...`
                    """,
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT"
            });

            // Add security definition for API Key authentication
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = "X-Api-Key",
                Description = """
                    API Key authentication using the X-Api-Key header.
                    Generate keys via POST /api/v1/admin/api-keys.

                    Example: `cm_live_AbCdEf...`
                    """,
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "ApiKey"
            });

            // Add security requirement - applies Bearer token globally to all operations
            // Individual endpoints can override this with [AllowAnonymous] attribute
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Bearer", document), [] },
                { new OpenApiSecuritySchemeReference("ApiKey", document), [] },
            });
        });

        return services;
    }

    /// <summary>
    /// Configures Swagger UI middleware.
    /// </summary>
    public static WebApplication UseSwaggerConfiguration(
        this WebApplication app,
        IConfiguration configuration)
    {
        IConfigurationSection swaggerConfig = configuration.GetSection("Swagger");
        bool swaggerEnabled = swaggerConfig.GetValue("Enabled", true);

        if (!swaggerEnabled)
        {
            return app;
        }

        // Enable Swagger JSON endpoint
        app.UseSwagger(options =>
        {
            options.RouteTemplate = "swagger/{documentName}/swagger.json";
        });

        // Configure Swagger UI
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "TradeFlow API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "TradeFlow API Documentation";

            // UI Customization
            options.DefaultModelsExpandDepth(1);
            options.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Example);
            options.DisplayOperationId();
            options.DisplayRequestDuration();
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
            options.EnableDeepLinking();
            options.EnableFilter();
            options.ShowExtensions();
            options.EnableValidator();

            // Persist authorization across page reloads
            options.EnablePersistAuthorization();
        });

        return app;
    }
}
