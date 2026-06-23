namespace Modulus.Data.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.Options;

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

        return services;
    }
}