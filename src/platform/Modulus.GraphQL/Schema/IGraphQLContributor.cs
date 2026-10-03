namespace Modulus.GraphQL;

using global::GraphQL.Types;

/// <summary>
/// A module's part of the one GraphQL schema: the fields it adds to the root <c>Query</c> and <c>Mutation</c> types.
/// Contributors run in registration order when the schema is built (once); a field name used twice fails startup.
/// Register one with <c>AddGraphQLContributor&lt;T&gt;()</c>, or let <c>AddModulusGraphQL(configuration, assemblies)</c>
/// find it. The graph types a contributor uses are registered from its assembly too.
/// </summary>
/// <remarks>
/// Resolvers run once per request: take scoped services from <c>context.RequestServices</c>
/// (<c>context.QueryAsync(...)</c> / <c>context.SendAsync(...)</c> go through the mediator), never through the
/// contributor's constructor, which runs once for the application.
/// </remarks>
public interface IGraphQLContributor
{
    /// <summary>Adds fields to the root <c>Query</c> type.</summary>
    void ConfigureQuery(ObjectGraphType query)
    {
    }

    /// <summary>Adds fields to the root <c>Mutation</c> type (it is left out of the schema when no module adds any).</summary>
    void ConfigureMutation(ObjectGraphType mutation)
    {
    }
}
