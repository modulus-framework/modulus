namespace Modulus.Data.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using NSubstitute;
using Xunit;

/// <summary>
/// MongoDB has no row-level security, so <see cref="TenantScopedCollection{T}"/> is where company isolation is
/// enforced: every filter is scoped, inserts are stamped, and updates cannot move a document between tenants.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantScopedCollectionTests
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly IMongoCollection<Invoice> _inner = Substitute.For<IMongoCollection<Invoice>>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly TenantScopedCollection<Invoice> _collection;

    public TenantScopedCollectionTests()
    {
        _inner.DocumentSerializer.Returns(BsonSerializer.LookupSerializer<Invoice>());
        _inner.Settings.Returns(new MongoCollectionSettings());
        _inner.CollectionNamespace.Returns(new CollectionNamespace("db", "invoices"));
        InTenant(TenantA);
        _collection = new TenantScopedCollection<Invoice>(_inner, _tenant);
    }

    private void InTenant(Guid? id, bool host = false)
    {
        _tenant.TenantId.Returns(id);
        _tenant.IsHost.Returns(host);
    }

    // Guids render as standard-representation binary, so compare against that encoding.
    private static readonly string TenantABinary = Convert.ToBase64String(TenantA.ToByteArray(bigEndian: true));

    private static BsonDocument Render(FilterDefinition<Invoice> filter)
        => filter.Render(new RenderArgs<Invoice>(BsonSerializer.LookupSerializer<Invoice>(), BsonSerializer.SerializerRegistry));

    [Fact]
    public void Unscoped_access_requires_a_reason_and_is_recorded_as_overridden()
    {
        var audit = Substitute.For<ISecurityAuditLog>();
        var collection = new TenantScopedCollection<Invoice>(_inner, _tenant, audit);

        collection.Invoking(c => c.Unscoped(" ")).Should().Throw<ArgumentException>();
        collection.Unscoped("index rebuild").Should().BeSameAs(_inner);

        audit.Received(1).Record(Arg.Is<SecurityAuditEvent>(e =>
            e.Category == SecurityAuditCategories.Data
            && e.Action == "mongo.unscoped"
            && e.Outcome == SecurityAuditOutcomes.Overridden
            && e.TenantId == TenantA
            && e.Target == "invoices"
            && e.Details!["reason"] == "index rebuild"));
    }

    [Fact]
    public async Task Deletes_and_counts_are_limited_to_the_ambient_tenant()
    {
        FilterDefinition<Invoice>? deleted = null, counted = null;
        await _inner.DeleteManyAsync(Arg.Do<FilterDefinition<Invoice>>(f => deleted = f), Arg.Any<CancellationToken>());
        await _inner.CountDocumentsAsync(
            Arg.Do<FilterDefinition<Invoice>>(f => counted = f), Arg.Any<CountOptions>(), Arg.Any<CancellationToken>());

        await _collection.DeleteManyAsync(Builders<Invoice>.Filter.Empty);
        await _collection.CountDocumentsAsync(Builders<Invoice>.Filter.Eq(i => i.Number, "7"));

        Render(deleted!).ToString().Should().Contain("TenantId").And.Contain(TenantABinary);
        Render(counted!).ToString().Should().Contain("TenantId").And.Contain("\"Number\" : \"7\"");
    }

    [Fact]
    public void Without_a_tenant_nothing_matches_and_the_host_sees_everything()
    {
        InTenant(null);
        Render(_collection.Scope()).ToString().Should().NotBe("{ }", "no tenant matches no documents");

        InTenant(null, host: true);
        Render(_collection.Scope()).Should().BeEmpty();
    }

    [Fact]
    public async Task Inserts_are_stamped_and_a_foreign_tenant_is_rejected()
    {
        var fresh = new Invoice();
        await _collection.InsertOneAsync(fresh);
        fresh.TenantId.Should().Be(TenantA);

        var act = () => _collection.InsertManyAsync([new Invoice(), new Invoice { TenantId = TenantB }]);

        (await act.Should().ThrowAsync<CrossTenantWriteException>()).Which.EntityTenantId.Should().Be(TenantB);
        await _inner.DidNotReceive().InsertManyAsync(
            Arg.Any<IEnumerable<Invoice>>(), Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_tenant_an_insert_is_rejected()
    {
        InTenant(null);

        var act = () => _collection.InsertOneAsync(new Invoice());

        await act.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Theory]
    [InlineData("set")]
    [InlineData("unset")]
    [InlineData("rename")]
    public async Task An_update_cannot_touch_the_tenant_field(string kind)
    {
        var update = kind switch
        {
            "set" => Builders<Invoice>.Update.Set(i => i.TenantId, TenantB),
            "unset" => Builders<Invoice>.Update.Unset(i => i.TenantId),
            _ => Builders<Invoice>.Update.Rename(i => i.Number, "TenantId"),
        };

        var act = () => _collection.UpdateManyAsync(Builders<Invoice>.Filter.Empty, update);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant field 'TenantId'*");
    }

    [Fact]
    public async Task Ordinary_updates_pass_and_the_host_may_move_a_document()
    {
        await _collection.UpdateOneAsync(Builders<Invoice>.Filter.Empty, Builders<Invoice>.Update.Set(i => i.Number, "8"));

        InTenant(null, host: true);
        await _collection.UpdateOneAsync(Builders<Invoice>.Filter.Empty, Builders<Invoice>.Update.Set(i => i.TenantId, TenantB));

        await _inner.Received(2).UpdateOneAsync(
            Arg.Any<FilterDefinition<Invoice>>(), Arg.Any<UpdateDefinition<Invoice>>(), Arg.Any<UpdateOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bulk_writes_scope_every_model()
    {
        IReadOnlyList<WriteModel<Invoice>>? sent = null;
        await _inner.BulkWriteAsync(
            Arg.Do<IEnumerable<WriteModel<Invoice>>>(m => sent = m.ToList()), Arg.Any<BulkWriteOptions>(), Arg.Any<CancellationToken>());
        var insert = new Invoice();

        await _collection.BulkWriteAsync(
        [
            new InsertOneModel<Invoice>(insert),
            new DeleteManyModel<Invoice>(Builders<Invoice>.Filter.Empty),
            new UpdateOneModel<Invoice>(Builders<Invoice>.Filter.Empty, Builders<Invoice>.Update.Set(i => i.Number, "9")) { IsUpsert = true },
        ]);

        insert.TenantId.Should().Be(TenantA);
        Render(((DeleteManyModel<Invoice>)sent![1]).Filter).ToString().Should().Contain(TenantABinary);
        var update = (UpdateOneModel<Invoice>)sent[2];
        update.IsUpsert.Should().BeTrue();
        Render(update.Filter).ToString().Should().Contain(TenantABinary, "an upsert inherits the tenant from the filter");
    }

    [Fact]
    public void Aggregations_start_with_a_match_on_the_tenant()
    {
        var pipeline = _collection.Aggregate().Group(i => i.Number, g => new { g.Key, Count = g.Count() }).ToString();

        pipeline.Should().StartWith("aggregate([{ \"$match\" : { \"TenantId\"").And.Contain(TenantABinary)
            .And.Contain("$group");
    }

    public sealed class Invoice : IHasTenantId
    {
        [BsonId]
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid TenantId { get; set; }

        public string Number { get; set; } = "1";
    }
}
