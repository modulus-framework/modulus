using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Domain.Entities;

/// <summary>
/// Represents a system permission that can be assigned to roles.
/// Permissions are used to control access to various system resources and operations.
/// </summary>
public sealed class Permission
{
    private Permission() { }

    private Permission(
        string code,
        string name,
        string description,
        string category,
        bool isActive = true)
    {
        Id = PermissionId.Create(code);
        Code = code;
        Name = name;
        Description = description;
        Category = category;
        IsActive = isActive;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Unique permission code identifier (e.g., "identity:user:view")
    /// </summary>
    public PermissionId Id { get; private set; } = default!;

    /// <summary>
    /// Permission code (same as Id for convenience)
    /// </summary>
    public string Code { get; private set; } = default!;

    /// <summary>
    /// Human-readable permission name
    /// </summary>
    public string Name { get; private set; } = default!;

    /// <summary>
    /// Detailed description of what the permission allows
    /// </summary>
    public string Description { get; private set; } = default!;

    /// <summary>
    /// Permission category for grouping (e.g., Users, Orders, Catalog)
    /// </summary>
    public string Category { get; private set; } = default!;

    /// <summary>
    /// Timestamp when permission was created
    /// </summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Indicates if this permission is currently active
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Creates a new permission instance
    /// </summary>
    /// <param name="code">Unique permission code</param>
    /// <param name="name">Human-readable name</param>
    /// <param name="description">Detailed description</param>
    /// <param name="category">Permission category</param>
    /// <param name="isActive">Whether the permission is active</param>
    /// <returns>New permission instance</returns>
    public static Result<Permission> Create(
        string code,
        string name,
        string description,
        string category,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.CodeRequired", "Permission code cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.NameRequired", "Permission name cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.DescriptionRequired", "Permission description cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure<Permission>(Error.Validation(
                "Permission.CategoryRequired", "Permission category cannot be empty"));
        }

        return Result.Success(new Permission(code, name, description, category, isActive));
    }

    /// <summary>
    /// Updates permission details
    /// </summary>
    /// <param name="name">New name</param>
    /// <param name="description">New description</param>
    /// <param name="category">New category</param>
    public Result UpdateDetails(string name, string description, string category)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Permission.NameRequired", "Permission name cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure(Error.Validation(
                "Permission.DescriptionRequired", "Permission description cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure(Error.Validation(
                "Permission.CategoryRequired", "Permission category cannot be empty"));
        }

        Name = name;
        Description = description;
        Category = category;

        return Result.Success();
    }

    /// <summary>
    /// Activates the permission
    /// </summary>
    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
    }

    /// <summary>
    /// Deactivates the permission
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
    }

    /// <summary>
    /// Gets the permission category from the permission code
    /// </summary>
    /// <param name="permissionCode">Permission code (e.g., "identity:user:view")</param>
    /// <returns>Category name (e.g., "Identity")</returns>
    public static string GetCategoryFromCode(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return "General";
        }

        string[] parts = permissionCode.Split(':');
        return parts.Length > 0 ? parts[0] : "General";
    }

    /// <summary>
    /// Validates permission code format
    /// </summary>
    /// <param name="code">Permission code to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValidCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        // Basic validation: should contain at least one colon and follow pattern like "category:action:resource"
        string[] parts = code.Split(':');
        return parts.Length >= 2 && Array.TrueForAll(parts, part => !string.IsNullOrWhiteSpace(part));
    }
}
