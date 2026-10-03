namespace Modulus.Data.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using NSubstitute;
using Testcontainers.MongoDb;
using Xunit;
using Invoice = TenantScopedCollectionTests.Invoice;

/// <summary>Wrapped operations against a real MongoDB never cross tenants (needs Docker).</summary>
[Trait("Category", "Integration")]
public sealed class TenantScopedCollectionIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7").Build();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private IMongoCollection<Invoice> _raw = null!;
    private TenantScopedCollection<Invoice> _invoices = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _raw = new MongoClient(_container.GetConnectionString()).GetDatabase("iso").GetCollection<Invoice>("invoices");
        _invoices = new TenantScopedCollection<Invoice>(_raw, _tenant);
        await _raw.InsertManyAsync(
        [
            new Invoice { TenantId = TenantA, Number = "a1" },
            new Invoice { TenantId = TenantA, Number = "a2" },
            new Invoice { TenantId = TenantB, Number = "b1" },
        ]);
        _tenant.TenantId.Returns(TenantB);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Reads_updates_deletes_aggregates_and_bulk_writes_stay_in_the_tenant()
    {
        (await _invoices.Find(Builders<Invoice>.Filter.Empty).ToListAsync()).Select(i => i.Number).Should().Equal("b1");
        (await _invoices.Aggregate().Count().SingleAsync()).Count.Should().Be(1);

        (await _invoices.UpdateManyAsync(Builders<Invoice>.Filter.Empty, Builders<Invoice>.Update.Set(i => i.Number, "x")))
            .ModifiedCount.Should().Be(1);
        await _invoices.BulkWriteAsync([new DeleteManyModel<Invoice>(Builders<Invoice>.Filter.Empty)]);
        (await _invoices.DeleteManyAsync(Builders<Invoice>.Filter.Eq(i => i.Number, "a1"))).DeletedCount.Should().Be(0);

        (await _raw.Find(i => i.TenantId == TenantA).CountDocumentsAsync()).Should().Be(2, "tenant A was never touched");
        (await _raw.Find(i => i.TenantId == TenantB).CountDocumentsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_upsert_lands_in_the_tenant()
    {
        await _invoices.UpdateOneAsync(
            Builders<Invoice>.Filter.Eq(i => i.Number, "new"),
            Builders<Invoice>.Update.Set(i => i.Number, "new"),
            new UpdateOptions { IsUpsert = true });

        (await _raw.Find(i => i.Number == "new").SingleAsync()).TenantId.Should().Be(TenantB);
    }
}
