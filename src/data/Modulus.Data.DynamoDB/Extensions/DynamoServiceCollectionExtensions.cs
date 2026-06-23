namespace Modulus.Data.DynamoDB.Extensions;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

public static class DynamoServiceCollectionExtensions
{
    public static IServiceCollection AddDynamoDbStore(
        this IServiceCollection services,
        Action<DynamoOptions> configure)
    {
        var opts = new DynamoOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            var config = new AmazonDynamoDBConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint
                    .GetBySystemName(opts.Region)
            };
            // Override for local dev (docker dynamodb-local)
            if (opts.ServiceUrl is not null)
                config.ServiceURL = opts.ServiceUrl;

            // Uses AWS SDK default credential chain
            return new AmazonDynamoDBClient(config);
        });

        services.AddScoped<IDynamoDBContext>(sp =>
            new DynamoDBContextBuilder()
                .WithDynamoDBClient(() => sp.GetRequiredService<IAmazonDynamoDB>())
                .Build());

        services.AddScoped<DynamoTableManager>();
        return services;
    }
}