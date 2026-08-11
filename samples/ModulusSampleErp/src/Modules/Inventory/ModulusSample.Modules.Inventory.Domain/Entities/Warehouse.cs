using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Inventory.Domain.Entities;

public sealed class Warehouse : AggregateRoot<Guid>, IHasOrgUnit
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string Country { get; private set; } = null!;

    public Guid OrgUnitId { get; private set; }
    public Guid TenantId { get; private set; }
    public bool IsActive { get; private set; }

    private Warehouse() { }

    public static Result<Warehouse> Create(
        Guid id, string code, string name, string address, string city,
        string postalCode, string country, Guid orgUnitId, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Warehouse>(Error.Validation("Warehouse.CodeRequired", "Code is required"));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Warehouse>(Error.Validation("Warehouse.NameRequired", "Name is required"));

        var warehouse = new Warehouse
        {
            Id = id,
            Code = code,
            Name = name,
            Address = address,
            City = city,
            PostalCode = postalCode,
            Country = country,
            OrgUnitId = orgUnitId,
            TenantId = tenantId,
            IsActive = true,
        };

        return Result.Success(warehouse);
    }
}
