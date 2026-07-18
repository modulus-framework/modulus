namespace Modulus.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Maps an entity CLR type to the module <see cref="DbContext"/> type that owns
/// it. In a modular monolith many module contexts coexist; this lets
/// <see cref="EfRepository{T}"/> resolve exactly the one context an entity
/// belongs to instead of instantiating <em>every</em> registered context and
/// scanning its model on each repository resolution.
/// </summary>
public interface IEntityContextMap
{
    /// <summary>
    /// Returns the <see cref="DbContext"/>-derived type whose model maps
    /// <paramref name="entityType"/>, or <c>null</c> when no registered module
    /// context owns it (the caller then falls back to a runtime scan).
    /// </summary>
    Type? Resolve(Type entityType);
}

/// <summary>
/// Accumulates the module context types registered through
/// <c>AddModuleDatabase&lt;TContext&gt;</c>. A single instance is stored in the
/// service collection at registration time (so each call can append to it) and
/// also resolved by <see cref="EntityContextMap"/> when the map is first built.
/// </summary>
internal sealed class EntityContextMapRegistry
{
    private readonly List<Type> _contextTypes = [];

    public void Register(Type contextType)
    {
        if (!_contextTypes.Contains(contextType))
            _contextTypes.Add(contextType);
    }

    public IReadOnlyList<Type> ContextTypes => _contextTypes;
}

/// <summary>
/// Default <see cref="IEntityContextMap"/>. Builds the entity→context lookup
/// <b>exactly once</b> for the application lifetime (singleton), the first time
/// a repository needs to route an entity, then serves O(1) dictionary lookups.
/// </summary>
internal sealed class EntityContextMap(
    IServiceProvider root,
    EntityContextMapRegistry registry) : IEntityContextMap
{
    // Built once. Building resolves each registered context a single time in a
    // throwaway scope to read its metadata model; OnModelCreating is pure
    // (model building only) so no database connection is opened.
    private readonly Lazy<IReadOnlyDictionary<Type, Type>> _map = new(
        () => Build(root, registry),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public Type? Resolve(Type entityType)
        => _map.Value.TryGetValue(entityType, out var contextType) ? contextType : null;

    private static IReadOnlyDictionary<Type, Type> Build(
        IServiceProvider root, EntityContextMapRegistry registry)
    {
        var map = new Dictionary<Type, Type>();
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;

        foreach (var contextType in registry.ContextTypes)
        {
            var context = (DbContext)sp.GetRequiredService(contextType);
            foreach (var entity in context.Model.GetEntityTypes())
            {
                // First registration wins. This mirrors the previous first-match
                // scan and keeps framework entities that every module context
                // maps (e.g. OutboxMessage) routed deterministically to the
                // first-registered context rather than throwing on ambiguity.
                map.TryAdd(entity.ClrType, contextType);
            }
        }

        return map;
    }
}
