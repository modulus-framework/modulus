namespace modulus.Modules.Catalog.Contracts.Dtos;

public sealed class ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
