namespace ModulusSample.Modules.Identity.Application.Permissions.Dtos;

/// <summary>
/// Response DTO for permission information
/// </summary>
public sealed record PermissionResponse(
    string Code,
    string Name,
    string Description,
    string Category,
    DateTime CreatedAtUtc,
    bool IsActive);

/// <summary>
/// Response DTO for permission list
/// </summary>
public sealed record PermissionListResponse(
    IEnumerable<PermissionResponse> Permissions);

/// <summary>
/// Response DTO for permission categories
/// </summary>
public sealed record PermissionCategoryResponse(
    string Category,
    int Count,
    IEnumerable<PermissionResponse> Permissions);
