using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Messaging.Sagas.EntityFrameworkCore;

public static class SagaEntityFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Stores <typeparamref name="TSaga"/> instances in <typeparamref name="TContext"/>, which must map <see cref="SagaRecord"/>
    /// through <c>ApplyModulusSagas</c>. Scoped, like the context it uses, so each message attempt uses its own.
    /// </summary>
    public static IServiceCollection AddEntityFrameworkSagaRepository<TSaga, TContext>(this IServiceCollection services)
        where TSaga : SagaInstance
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<ISagaRepository<TSaga>>(sp => new EfSagaRepository<TSaga>(sp.GetRequiredService<TContext>()));
        return services;
    }
}
