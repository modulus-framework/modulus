using ProcureFlow.Modules.Identity.Domain.Entities;

namespace ProcureFlow.Modules.Identity.Domain.Repositories;

/// <summary>
/// Repository interface for managing Permission entities.
/// </summary>
public interface IPermissionRepository
{
    /// <summary>
    /// Gets a permission by its code.
    /// </summary>
    /// <param name="code">The permission code</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The permission if found, otherwise null</returns>
    Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Gets all permissions.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection of all permissions</returns>
    Task<IEnumerable<Permission>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets permissions by category.
    /// </summary>
    /// <param name="category">The permission category</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection of permissions in the specified category</returns>
    Task<IEnumerable<Permission>> GetByCategoryAsync(string category, CancellationToken ct = default);

    /// <summary>
    /// Gets active permissions only.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection of active permissions</returns>
    Task<IEnumerable<Permission>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets permissions by their codes.
    /// </summary>
    /// <param name="codes">Collection of permission codes</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Collection of permissions matching the provided codes</returns>
    Task<IEnumerable<Permission>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken ct = default);

    /// <summary>
    /// Checks if a permission exists by its code.
    /// </summary>
    /// <param name="code">The permission code</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the permission exists, otherwise false</returns>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Gets the count of permissions.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Total count of permissions</returns>
    Task<int> GetCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the count of active permissions.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Count of active permissions</returns>
    Task<int> GetActiveCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new permission.
    /// </summary>
    /// <param name="permission">The permission to add</param>
    void Add(Permission permission);

    /// <summary>
    /// Updates an existing permission.
    /// </summary>
    /// <param name="permission">The permission to update</param>
    void Update(Permission permission);

    /// <summary>
    /// Removes a permission.
    /// </summary>
    /// <param name="permission">The permission to remove</param>
    void Remove(Permission permission);
}
