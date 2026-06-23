namespace Modulus.Data.Cassandra.Extensions;

using global::Cassandra;
using Microsoft.Extensions.Options;

public static class CassandraServiceCollectionExtensions
{
    public static IServiceCollection AddCassandraStore(
        this IServiceCollection services,
        Action<CassandraOptions> configure)
    {
        var opts = new CassandraOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<ICluster>(_ =>
            Cluster.Builder()
                .AddContactPoints(opts.ContactPoints)
                .WithPort(opts.Port)
                .WithLoadBalancingPolicy(
                    new DCAwareRoundRobinPolicy(opts.Datacenter))
                .Build());

        services.AddSingleton<ISession>(sp =>
            sp.GetRequiredService<ICluster>()
              .Connect(opts.Keyspace));

        services.AddSingleton<PreparedStatementCache>();
        services.AddScoped<CassandraTableManager>();

        return services;
    }
}