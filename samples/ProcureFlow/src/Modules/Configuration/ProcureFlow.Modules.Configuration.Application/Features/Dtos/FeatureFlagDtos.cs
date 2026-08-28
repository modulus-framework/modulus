namespace ProcureFlow.Modules.Configuration.Application.Features.Dtos;

public sealed record FeatureFlagResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    Guid TenantId,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime LastModifiedAt,
    string? LastModifiedBy);

public sealed record CreateFeatureFlagResponse(
    Guid FeatureFlagId,
    string Key,
    string Name);

public sealed record UpdateFeatureFlagResponse(
    Guid FeatureFlagId,
    string Key,
    string Name,
    bool IsEnabled,
    DateTime UpdatedAtUtc);
