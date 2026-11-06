namespace Modulus.Analytics.Debezium;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Debezium change reader and ingestor.</summary>
public static class DebeziumServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reader for the given source tables. Invalid mappings throw here, at startup. Connector settings are in
    /// <c>docs/runbooks/lakehouse-cdc.md</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="mappings">One mapping per source table.</param>
    public static IServiceCollection AddModulusDebezium(this IServiceCollection services, IEnumerable<DebeziumTableMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(services);
        var reader = new DebeziumChangeReader(mappings);
        services.AddSingleton(reader);
        services.AddScoped<DebeziumIngestor>();
        return services;
    }
}
