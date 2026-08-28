using Microsoft.Extensions.DependencyInjection;

namespace Modulus.EntityFrameworkCore.ChangeHistory;

/// <summary>
/// Registration extensions for entity change history (field-level audit trails).
/// </summary>
public static class EntityChangeHistoryExtensions
{
    /// <summary>
    /// Registers IEntityChangeHistoryWriter to capture field-level
    /// changes to entities marked with [Audited].
    ///
    /// When enabled, every auditable entity row creation/modification/deletion
    /// results in EntityChange records being written alongside the entity,
    /// answering "who changed this invoice's amount?" — not just "who last updated it?"
    /// </summary>
    public static IServiceCollection AddEntityChangeHistory(
        this IServiceCollection services)
    {
        services.AddScoped<IEntityChangeHistoryWriter, EntityChangeHistoryWriter>();
        return services;
    }
}
