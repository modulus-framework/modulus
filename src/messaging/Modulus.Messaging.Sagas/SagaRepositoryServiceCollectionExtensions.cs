using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Messaging.Sagas;

public static class SagaRepositoryServiceCollectionExtensions
{
    /// <summary>Registers an in-memory repository for <typeparamref name="TSaga"/> unless the application registers its own first.</summary>
    public static IServiceCollection AddInMemorySagaRepository<TSaga>(this IServiceCollection services)
        where TSaga : SagaInstance
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISagaRepository<TSaga>, InMemorySagaRepository<TSaga>>();
        return services;
    }
}
