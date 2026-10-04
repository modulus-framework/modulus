namespace Modulus.EntityFrameworkCore.Saving;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Adds rows to a <see cref="ModuleDbContext"/>'s unit of work while it saves, so they commit in the same transaction
/// as the entity writes that caused them (a journal, a feed). The write-side twin of
/// <see cref="ModelBuilding.IModuleModelContributor"/>, which maps the rows' entity into every module context.
/// </summary>
/// <remarks>
/// Runs inside <c>SaveChangesAsync</c> after audit fields, soft-delete conversion, tenant stamping and the cross-tenant
/// write guard, and before the outbox rows are added, so a soft delete is seen as a <c>Modified</c> entry with
/// <c>IsDeleted</c> set. Register with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IModuleSaveContributor, T&gt;())</c>; implementations are
/// called for every context, must be stateless and thread-safe, and must only add rows of entities the context maps.
/// </remarks>
public interface IModuleSaveContributor
{
    /// <summary>Adds this contributor's rows for the pending changes of <paramref name="context"/>.</summary>
    /// <param name="context">The context that is saving.</param>
    /// <param name="tenantId">The ambient company, or null in the host context.</param>
    void OnSaving(DbContext context, Guid? tenantId);
}
