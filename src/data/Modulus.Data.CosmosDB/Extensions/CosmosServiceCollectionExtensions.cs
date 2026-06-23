namespace Modulus.Data.CosmosDB.Extensions;

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

public static class CosmosServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDbStore(
        this IServiceCollection services,
        Action<CosmosOptions> configure)
    {
        var opts = new CosmosOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<CosmosClient>(_ =>
            new CosmosClient(opts.AccountEndpoint, opts.AccountKey,
                new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase }
                }));

        services.AddScoped<CosmosContainerManager>();
        return services;
    }
}