using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Messaging.Outbox.Npgsql;
using Npgsql;

namespace Modulus.Messaging.Sagas.PostgreSql;

public static class SagaPostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers a JSONB repository for <typeparamref name="TSaga"/> on the data source, unless the application registers its
    /// own. The repository is scoped so it joins the scope's unit of work (RULE-06): call <c>AddModulusMessagingNpgsqlUnitOfWork</c>
    /// (or the Npgsql outbox) as well.
    /// </summary>
    public static IServiceCollection AddPostgreSqlSagaRepository<TSaga>(this IServiceCollection services, string table = "messaging_sagas")
        where TSaga : SagaInstance
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ISagaRepository<TSaga>>(sp =>
            new PostgreSqlSagaRepository<TSaga>(sp.GetRequiredService<NpgsqlDataSource>(), table, sp.GetService<INpgsqlUnitOfWork>()));
        return services;
    }
}
