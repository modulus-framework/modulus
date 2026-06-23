namespace Modulus.Data.Elasticsearch;

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Data.Abstractions;
// Disambiguate our SearchRequest from Elastic.Clients.Elasticsearch.SearchRequest
using SearchRequest = Modulus.Data.Abstractions.SearchRequest;

/// <summary>
/// Base ISearchRepository{T} implementation for Elasticsearch.
/// Extend and set IndexName. Override SearchAsync for custom queries.
/// </summary>
public abstract class ElasticRepository<T>(
    ElasticsearchClient        client,
    IOptions<ElasticsearchOptions> opts,
    ICurrentTenant             tenant)
    : ISearchRepository<T>
    where T : class
{
    protected readonly ElasticsearchClient  Client  = client;
    protected readonly ElasticsearchOptions Options = opts.Value;
    protected Guid TenantId => tenant.TenantId ?? Guid.Empty;

    protected abstract string IndexName { get; }

    public virtual async Task<SearchResult<T>> SearchAsync(
        SearchRequest request, CancellationToken ct)
    {
        var from = (request.Page - 1) * request.PageSize;

        var resp = await Client.SearchAsync<T>(s => s
            .Indices(IndexName)
            .From(from).Size(request.PageSize)
            .Query(q => q
                .Bool(b => b
                    .Must(m => m
                        .MultiMatch(mm => mm
                            .Query(request.Term)
                            .Type(TextQueryType.BestFields)))
                    .Filter(f => f
                        .Term(t => t
                            .Field("tenantId")
                            .Value(TenantId.ToString()))))),
            ct);

        return new SearchResult<T>(
            Items:      resp.Documents.ToList(),
            TotalCount: resp.Total,
            Page:       request.Page,
            PageSize:   request.PageSize,
            TookMs:     resp.Took);
    }

    public Task IndexAsync(T document, CancellationToken ct)
        => Client.IndexAsync(document,
            i => i.Index(IndexName), ct);

    public Task IndexBulkAsync(
        IEnumerable<T> documents, CancellationToken ct)
        => Client.BulkAsync(b => b
            .Index(IndexName)
            .IndexMany(documents), ct);

    public Task DeleteFromIndexAsync(object id, CancellationToken ct)
        => Client.DeleteAsync(IndexName,
            id.ToString()!, ct);
}