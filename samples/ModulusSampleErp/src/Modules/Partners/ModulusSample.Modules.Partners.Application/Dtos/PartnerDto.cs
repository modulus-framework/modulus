namespace ModulusSample.Modules.Partners.Application.Dtos;

public sealed record PartnerDto(
    Guid Id,
    string Name,
    string Type,
    string Email,
    string Phone,
    string Address,
    Guid OwnerId,
    Guid TenantId,
    bool IsActive);
