using Modulus.Core.Abstractions.Domain;

namespace modulus.Modules.Catalog.Domain;

/// <summary>
/// Sample aggregate root. Replace with your domain entity.
/// </summary>
public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; set; } = string.Empty;
}
