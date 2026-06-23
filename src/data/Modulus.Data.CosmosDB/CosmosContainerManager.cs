using System.ComponentModel;

namespace Modulus.Data.CosmosDB;

using Microsoft.Azure.Cosmos;

public sealed class CosmosContainerManager(
    CosmosClient                 client,
    IOptions<CosmosOptions>      opts)
{
    public async Task<Container> EnsureContainerAsync(
        string containerName,
        string? partitionKeyPath = null,
        CancellationToken ct = default)
    {
        var db = await client.CreateDatabaseIfNotExistsAsync(
            opts.Value.DatabaseId, cancellationToken: ct);

        var fullName = opts.Value.ContainerPrefix + containerName;
        var pkPath   = partitionKeyPath ?? opts.Value.PartitionKeyPath;

        var resp = await db.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(fullName, pkPath), cancellationToken: ct);

        return resp.Container;
    }
}