namespace Modulus.Data.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization.IdGenerators;
using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.Data.Abstractions;
using NSubstitute;
using Testcontainers.MongoDb;
using Xunit;

/// <summary>D1 and D6: paged deletes delete exactly the page, and listing sorts before it pages.</summary>
[Trait("Category", "Integration")]
public sealed class MongoRepositoryPagingTests : IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7").Build();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private TicketRepository _repository = null!;
    private IMongoCollection<TicketDocument> _collection = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _collection = new MongoClient(_container.GetConnectionString()).GetDatabase("paging").GetCollection<TicketDocument>("tickets");
        _tenant.TenantId.Returns(Tenant);
        _repository = new TicketRepository(_collection, _tenant);
        for (var i = 1; i <= 10; i++)
            await _collection.InsertOneAsync(new TicketDocument { TenantId = Tenant, Seq = i });
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Deleting_a_page_removes_only_that_page()
    {
        // The oldest three (by Seq) are the page; a filter-only delete would have taken all ten.
        await _repository.DeleteRangeAsync(new BySeq(take: 3), CancellationToken.None);

        var remaining = (await _collection.Find(FilterDefinition<TicketDocument>.Empty).ToListAsync()).Select(d => d.Seq).OrderBy(s => s).ToList();
        remaining.Should().Equal(4, 5, 6, 7, 8, 9, 10);
    }

    [Fact]
    public async Task Listing_sorts_before_it_pages_when_the_filter_is_not_translated()
    {
        // A filter the repository does not translate is evaluated client-side; the sort must still happen before Take.
        var page = await _repository.ListAsync(new BySeq(take: 2, filterTo: 8, descending: true), CancellationToken.None);

        page.Select(t => t.Seq).Should().Equal(8, 7);
    }

    [Fact]
    public async Task Listing_sorts_before_it_pages_when_the_filter_is_translated_but_the_sort_is_not()
    {
        // The server path is only correct when the sort is server-side too. With the filter translated and the sort not,
        // paging on the server would return the first two matches in natural order (2, 3) instead of (8, 7).
        var repository = new TicketRepository(_collection, _tenant, translateFilter: true);

        var page = await repository.ListAsync(new BySeq(take: 2, filterTo: 8, descending: true), CancellationToken.None);

        page.Select(t => t.Seq).Should().Equal(8, 7);
    }

    public sealed class TicketDocument : IHasTenantId
    {
        [BsonId(IdGenerator = typeof(GuidGenerator))]
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid TenantId { get; set; }

        public int Seq { get; set; }
    }

    public sealed class Ticket : AggregateRoot
    {
        public Ticket(Guid id, Guid tenantId, int seq)
        {
            Id = id;
            TenantId = tenantId;
            Seq = seq;
        }

        public Guid TenantId { get; }

        public int Seq { get; }
    }

    private sealed class TicketRepository(IMongoCollection<TicketDocument> collection, ICurrentTenant tenant, bool translateFilter = false)
        : MongoRepository<Ticket, TicketDocument>(collection, tenant)
    {
        protected override Ticket ToDomain(TicketDocument doc) => new(doc.Id, doc.TenantId, doc.Seq);

        protected override TicketDocument ToDocument(Ticket entity) => new() { Id = entity.Id, TenantId = entity.TenantId, Seq = entity.Seq };

        // The filter can be translated to the server while the sort cannot (BuildDocumentSort stays null).
        protected override FilterDefinition<TicketDocument>? BuildDocumentFilter(ISpecification<Ticket> spec) =>
            translateFilter && spec is BySeq { FilterTo: { } to }
                ? Builders<TicketDocument>.Filter.Lte(d => d.Seq, to)
                : null;
    }

    private sealed class BySeq : Specification<Ticket>
    {
        public BySeq(int? take = null, int? filterTo = null, bool descending = false)
        {
            FilterTo = filterTo;
            if (filterTo is { } to)
                Filter = t => t.Seq <= to;
            AddOrderBy(t => t.Seq, descending);
            Take = take;
        }

        public int? FilterTo { get; }
    }
}
