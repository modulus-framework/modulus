namespace Modulus.Data.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

public static class MongoServiceCollectionExtensions
{
    public static IServiceCollection AddMongoDatabase(
        this IServiceCollection services,
        Action<MongoOptions> configure)
    {
        var opts = new MongoOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<IMongoClient>(_ =>
            new MongoClient(opts.ConnectionString));

        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>()
              .GetDatabase(opts.DatabaseName));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IModuleHealthCheck, MongoHealthCheck>());

        return services;
    }
}
