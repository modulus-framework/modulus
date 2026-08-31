using System.Text.Json;

namespace TradeFlow.Modules.Tenants.Application.Tenants.Dtos;

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Subdomain,
    bool IsActive,
    JsonDocument Features,
    JsonDocument Settings,
    DateTime CreatedAtUtc,
    string? CreatedBy,
    DateTime? LastModifiedAtUtc,
    string? LastModifiedBy,
    bool IsDeleted,
    DateTime? DeletedAtUtc,
    string? DeletedBy,
    long Version);

public sealed record CreateTenantResponse(
    Guid TenantId,
    string Name,
    string Subdomain);

public sealed record UpdateTenantResponse(
    Guid TenantId,
    string Name,
    DateTime UpdatedAtUtc);

public sealed record TenantStatusResponse(
    Guid TenantId,
    string Name,
    bool IsActive,
    DateTime StatusChangedAtUtc);
