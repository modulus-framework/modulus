namespace Modulus.AspNetCore.OpenApi;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Adds a Bearer security requirement to operations whose endpoint carries an
/// authorization policy, unless it also allows anonymous access. This lets UIs
/// mark secured endpoints (padlock) without forcing a token on public ones such
/// as the token, health, or OpenAPI endpoints.
/// </summary>
internal sealed class AuthorizeCheckOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var requiresAuth =
            metadata.OfType<IAuthorizeData>().Any() &&
            !metadata.OfType<IAllowAnonymous>().Any();

        if (requiresAuth)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ModulusOpenApiDocumentTransformer.BearerSchemeName, null)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
