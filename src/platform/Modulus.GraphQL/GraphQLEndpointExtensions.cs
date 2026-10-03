namespace Modulus.GraphQL;

using global::GraphQL.Server.Ui.GraphiQL;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Security;

/// <summary>Maps the GraphQL endpoint.</summary>
public static class GraphQLEndpointExtensions
{
    /// <summary>
    /// Maps the schema at <paramref name="path"/> (default <see cref="ModulusGraphQLOptions.Path"/>, <c>/graphql</c>):
    /// GET and JSON POST, CSRF protection on (a GET or a form-encoded POST needs a <c>GraphQL-Require-Preflight</c>
    /// header), no form posts, batches only when enabled, and <c>401</c> for an anonymous request unless
    /// <see cref="ModulusGraphQLOptions.RequireAuthenticatedUser"/> is off. It runs through the normal pipeline, so
    /// correlation, tenant resolution and authentication apply. Mapped on a route group (a BFF client's
    /// <c>MapBffClient(...)</c>, say), the group's conventions apply too. With
    /// <see cref="ModulusGraphQLOptions.EnableUi"/> the GraphiQL IDE is mapped at <c>{path}/ui</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapModulusGraphQL(this IEndpointRouteBuilder endpoints, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (endpoints.ServiceProvider.GetService<ModulusSchema>() is null)
            throw new InvalidOperationException("MapModulusGraphQL needs services.AddModulusGraphQL(configuration, ...) first.");

        var options = GraphQLServiceCollectionExtensions.Options(endpoints.ServiceProvider);
        var route = (path ?? options.Path).TrimEnd('/');
        if (route.Length == 0)
            route = "/";

        var endpoint = endpoints.MapGraphQL<ModulusSchema>(route, http =>
        {
            http.EnableBatchedRequests = options.EnableBatchedRequests;
            http.ExecuteBatchedRequestsInParallel = false;
            http.HandleWebSockets = false;
            http.ReadFormOnPost = false;
            http.CsrfProtectionEnabled = true;
            http.AuthorizationRequired = options.RequireAuthenticatedUser;
        });

        // No AllowAnonymous here even when RequireAuthenticatedUser is off: endpoint-level anonymity would override
        // the policy of an enclosing group (a BFF client's). Under a fallback policy, an app that wants anonymous
        // GraphQL opens it on the returned builder: MapModulusGraphQL().Loosen("...").

        if (options.EnableUi)
        {
            // Relative, so the IDE finds the endpoint under whatever group prefix it is mapped on.
            var lastSegment = route.Length > 1 ? route[(route.LastIndexOf('/') + 1)..] : string.Empty;
            endpoints.MapGraphQLGraphiQL(route == "/" ? "/ui" : route + "/ui", new GraphiQLOptions
            {
                GraphQLEndPoint = route == "/" ? "../" : "../" + lastSegment,
                HeaderEditorEnabled = true,
            }).AllowAnonymous()
                .WithMetadata(new LoosenedAttribute("GraphiQL IDE page (opted in through GraphQL:EnableUi); queries still authorize") { Framework = true });
        }

        return endpoint;
    }
}
