namespace Modulus.Core.Null;

using Modulus.Core.Abstractions.Entities;

/// <summary>The default catalog: no extension field is declared, so every extension value write is refused.</summary>
public sealed class NullExtensionFieldCatalog : IExtensionFieldCatalog
{
    public static readonly NullExtensionFieldCatalog Instance = new();

    public FieldClassification? Find(Type entityType, string name) => null;
}
