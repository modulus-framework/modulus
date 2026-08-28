namespace Modulus.EntityFrameworkCore.ModelBuilding;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Contributes entity mappings to every <see cref="ModuleDbContext"/>'s model.
/// Lets feature packages (Inbox, Outbox, …) map their infrastructure entities
/// into module contexts without <c>Modulus.EntityFrameworkCore</c> taking a
/// reference on them (which would be circular — they reference it).
/// </summary>
/// <remarks>
/// Contributors run before table-prefixing, so tables they map receive the
/// owning module's prefix (e.g. <c>cat_inbox_messages</c>), exactly like the
/// built-in outbox mapping. Register via
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IModuleModelContributor, T&gt;())</c>;
/// implementations must be stateless and thread-safe (a single instance serves
/// every context).
/// </remarks>
public interface IModuleModelContributor
{
    /// <summary>Applies this contributor's entity configuration to the model.</summary>
    void Contribute(ModelBuilder modelBuilder);
}
