namespace ProcureFlow.Modules.Configuration.Application.Settings.Dtos;

public sealed record SettingResponse(
    Guid Id,
    string Key,
    string Value,
    string Category,
    string Description,
    bool IsPublic,
    Guid TenantId,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime? LastModifiedAt,
    string? LastModifiedBy);
