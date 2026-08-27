using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Domain.Entities;

public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>The cost to acquire this product (marked [Classified] for field-level security demo).</summary>
    [Classified(FieldClassification.Confidential)]
    public decimal UnitCost { get; private set; }

    /// <summary>The margin/markup on the product (marked [Classified] for field-level security demo).</summary>
    [Classified(FieldClassification.Confidential)]
    public decimal? Margin { get; private set; }

    public decimal ListPrice { get; private set; }
    public Guid? CategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public Guid TenantId { get; private set; }

    // For EF Core
    private Product() { }

    public static Result<Product> Create(
        Guid id, string name, decimal unitCost, decimal listPrice,
        Guid tenantId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Product>(Error.Validation("Product.NameRequired", "Name is required"));
        if (unitCost < 0)
            return Result.Failure<Product>(Error.Validation("Product.UnitCostNegative", "Unit cost cannot be negative"));
        if (listPrice < 0)
            return Result.Failure<Product>(Error.Validation("Product.ListPriceNegative", "List price cannot be negative"));

        var product = new Product
        {
            Id = id,
            Name = name,
            UnitCost = unitCost,
            ListPrice = listPrice,
            TenantId = tenantId,
            IsActive = true,
        };

        return Result.Success(product);
    }
}
