namespace Modulus.GraphQL;

using global::GraphQL;
using global::GraphQL.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Security;

/// <summary>
/// Lists every root field (<c>Query.*</c>, <c>Mutation.*</c>) in the startup security guard's report: a field with
/// <c>AuthorizeWithPolicy</c> / <c>AuthorizeWithRoles</c> / <c>Authorize</c> is policed, any other inherits the GraphQL
/// endpoint's sign-in requirement, or is anonymous when <see cref="ModulusGraphQLOptions.RequireAuthenticatedUser"/> is
/// off. Nested fields are not listed: they are reached through a root field.
/// </summary>
internal sealed class GraphQLSecuritySurface : ISecuritySurfaceContributor
{
    public IEnumerable<SecuritySurfaceEntry> Describe(IServiceProvider services)
    {
        var signedIn = services.GetRequiredService<IOptions<ModulusGraphQLOptions>>().Value.RequireAuthenticatedUser;
        var schema = services.GetRequiredService<ModulusSchema>();
        foreach (var root in new IObjectGraphType?[] { schema.Query, schema.Mutation })
        {
            if (root is null)
                continue;
            foreach (var field in root.Fields)
            {
                List<string> policies = [.. field.GetPolicies() ?? [], .. (field.GetRoles() ?? []).Select(r => $"role:{r}")];
                if (policies.Count == 0 && field.IsAuthorizationRequired())
                    policies.Add("(authenticated)");

                var access = policies.Count > 0 ? SecuritySurfaceAccess.Policed
                    : signedIn ? SecuritySurfaceAccess.Inherited
                    : SecuritySurfaceAccess.Anonymous;
                yield return new SecuritySurfaceEntry("graphql", $"{root.Name}.{field.Name}", access, policies);
            }
        }
    }
}
