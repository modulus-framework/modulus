using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Domain.Entities;

public sealed class Partner : AggregateRoot<Guid>, IHasOwner
{
    public string Name { get; private set; } = null!;
    public string Type { get; private set; } = null!; // "Customer" or "Supplier"

    public string Email { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string Address { get; private set; } = null!;

    public Guid OwnerId { get; private set; }
    public Guid TenantId { get; private set; }
    public bool IsActive { get; private set; }

    private Partner() { }

    public static Result<Partner> Create(
        Guid id, string name, string type, string email, string phone, string address,
        Guid ownerId, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Partner>(Error.Validation("Partner.NameRequired", "Name is required"));
        if (type is not "Customer" and not "Supplier")
            return Result.Failure<Partner>(Error.Validation("Partner.InvalidType", "Type must be Customer or Supplier"));
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<Partner>(Error.Validation("Partner.EmailRequired", "Email is required"));

        var partner = new Partner
        {
            Id = id,
            Name = name,
            Type = type,
            Email = email,
            Phone = phone,
            Address = address,
            OwnerId = ownerId,
            TenantId = tenantId,
            IsActive = true,
        };

        return Result.Success(partner);
    }
}
