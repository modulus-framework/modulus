using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Modulus.Messaging.Sagas.PostgreSql;

public static class SagaPostgreSqlServiceCollectionExtensions
{
    /// <summary>Registers a JSONB repository for <typeparamref name="TSaga"/> on the data source, unless the application registers its own.</summary>
    public static IServiceCollection AddPostgreSqlSagaRepository<TSaga>(this IServiceCollection services, string table = "messaging_sagas")
        where TSaga : SagaInstance
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISagaRepository<TSaga>>(sp =>
            new PostgreSqlSagaRepository<TSaga>(sp.GetRequiredService<NpgsqlDataSource>(), table));
        return services;
    }
}
