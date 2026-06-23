namespace Modulus.Data.Elasticsearch;

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

/// <summary>
/// Creates indexes with proper mappings during module InitializeAsync.
/// </summary>
public sealed class ElasticsearchIndexManager(
    ElasticsearchClient client)
{
    public async Task EnsureIndexAsync<T>(
        string indexName,
        Action<TypeMappingDescriptor<T>>? mappings = null,
        CancellationToken ct = default)
        where T : class
    {
        var exists = await client.Indices.ExistsAsync(indexName, ct);
        if (exists.Exists) return;

        await client.Indices.CreateAsync<T>(indexName, i =>
        {
            i.Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(1));

            if (mappings is not null)
                i.Mappings(m => mappings(m));
        }, ct);
    }
}