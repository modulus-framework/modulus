namespace Modulus.GraphQL;

using global::GraphQL.Types;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The application's one GraphQL schema: a <c>Query</c> and a <c>Mutation</c> root type made of the fields every
/// registered <see cref="IGraphQLContributor"/> adds, in registration order. Built once, at the first request.
/// </summary>
public sealed class ModulusSchema : Schema
{
    /// <summary>Builds the root types from <paramref name="services"/>' contributors.</summary>
    public ModulusSchema(IServiceProvider services)
        : base(services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var contributors = services.GetServices<IGraphQLContributor>().ToList();

        var query = new ObjectGraphType { Name = "Query" };
        var mutation = new ObjectGraphType { Name = "Mutation" };
        foreach (var contributor in contributors)
        {
            Configure(contributor, query, "Query", c => c.ConfigureQuery(query));
            Configure(contributor, mutation, "Mutation", c => c.ConfigureMutation(mutation));
        }

        if (query.Fields.Count == 0)
        {
            throw new InvalidOperationException(
                "The GraphQL schema has no query field: register an IGraphQLContributor (AddGraphQLContributor<T>(), or pass its "
                + "assembly to AddModulusGraphQL) that adds one in ConfigureQuery. GraphQL requires at least one.");
        }

        Query = query;
        if (mutation.Fields.Count > 0)
            Mutation = mutation;
    }

    private static void Configure(IGraphQLContributor contributor, ObjectGraphType root, string rootName, Action<IGraphQLContributor> configure)
    {
        try
        {
            configure(contributor);
        }
        catch (ArgumentException ex)
        {
            // GraphQL.NET rejects a second field with the same name; say which module added it.
            throw new InvalidOperationException(
                $"GraphQL contributor {contributor.GetType().FullName} could not configure {rootName}: {ex.Message} "
                + $"Fields already on {rootName}: {string.Join(", ", root.Fields.Select(f => f.Name))}.", ex);
        }
    }
}

/// <summary>A change one module makes to another module's graph type (see <c>ExtendGraphType</c>).</summary>
internal sealed record GraphTypeExtension(Type GraphType, Action<object> Apply);
