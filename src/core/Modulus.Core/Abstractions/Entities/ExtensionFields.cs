namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Looks up the declared classification of an extension field (a key of <see cref="IHasExtraProperties.ExtraProperties"/>).
/// A write stores a value only under a key that is declared here, so an extension value always carries a sensitivity
/// the field-security layer can enforce. Without a registered catalog every extension value is refused.
/// </summary>
public interface IExtensionFieldCatalog
{
    /// <summary>The classification of <paramref name="name"/> on <paramref name="entityType"/> (or an ancestor), or null when undeclared.</summary>
    FieldClassification? Find(Type entityType, string name);
}

/// <summary>
/// The declared extension fields of an application. Build it once at startup and register it as a singleton:
/// <c>services.AddSingleton&lt;IExtensionFieldCatalog&gt;(new ExtensionFieldCatalog().Register&lt;Product&gt;("Bin", FieldClassification.Internal))</c>.
/// </summary>
public sealed class ExtensionFieldCatalog : IExtensionFieldCatalog
{
    private readonly Dictionary<(Type Entity, string Name), FieldClassification> _fields = [];

    /// <summary>Declares <paramref name="name"/> on <typeparamref name="TEntity"/> with its classification.</summary>
    /// <exception cref="ArgumentException">The name is blank or already declared on the entity.</exception>
    public ExtensionFieldCatalog Register<TEntity>(string name, FieldClassification classification)
        where TEntity : IHasExtraProperties
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_fields.TryAdd((typeof(TEntity), name), classification))
            throw new ArgumentException($"Extension field '{name}' is already declared on {typeof(TEntity).Name}.", nameof(name));

        return this;
    }

    /// <inheritdoc />
    public FieldClassification? Find(Type entityType, string name)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var type = entityType; type is not null; type = type.BaseType)
        {
            if (_fields.TryGetValue((type, name), out var classification))
                return classification;
        }

        return null;
    }
}
