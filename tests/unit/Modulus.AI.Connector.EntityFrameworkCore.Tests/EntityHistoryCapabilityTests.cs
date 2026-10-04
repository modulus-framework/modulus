namespace Modulus.AI.Connector.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.ChangeHistory;
using Xunit;

[Trait("Category", "Unit")]
public sealed class EntityHistoryCapabilityTests
{
    private static readonly string Key = Guid.NewGuid().ToString("D");

    private sealed class FakeReader : IEntityChangeHistoryReader
    {
        public EntityChangeQuery? Last { get; private set; }

        public Task<IReadOnlyList<EntityChange>> QueryAsync(EntityChangeQuery query, CancellationToken ct = default)
        {
            Last = query;
            IReadOnlyList<EntityChange> changes =
            [
                Change(nameof(Product.Name), "Bolt", "Bolt M6"),
                Change(nameof(Product.Cost), "1.00", "1.20"),
                Change(nameof(Product.Token), "old-secret", "new-secret"),
                Change(nameof(Product.Buyer), "ann", "bob"),
                Change("Removed", "a", "b"),
            ];
            return Task.FromResult(changes);
        }

        private static EntityChange Change(string property, string from, string to) => new()
        {
            EntityName = nameof(Product),
            EntityKey = Key,
            PropertyName = property,
            OriginalValue = from,
            NewValue = to,
            ChangedBy = "u1",
            Operation = "Update",
            ChangedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
        };
    }

    private sealed class NoProfiles : IFieldSecurityRegistry
    {
        public FieldSecurityProfile? Find(Type resourceType) => null;
    }

    private static async Task<(IReadOnlyList<EntityChangeRecord> Records, FakeReader Reader)> RunAsync(
        ListEntityChanges query, bool withFieldAuthorizer)
    {
        var reader = new FakeReader();
        await using var host = new JournalTestHost(services =>
        {
            if (withFieldAuthorizer)
                services.AddScoped<IFieldAuthorizer>(sp => new FieldAuthorizer(new NullCurrentUser(), new NoProfiles()));
        });

        var records = await host.InAsync(JournalTestHost.CompanyA, sp =>
            new ListEntityChangesHandler(reader, sp.GetRequiredService<EfAiEntitySource>(), sp).HandleAsync(query, CancellationToken.None));
        return (records, reader);
    }

    [Fact]
    public async Task The_query_is_mapped_to_the_entity_and_bounded()
    {
        var (_, reader) = await RunAsync(new ListEntityChanges("Shop.Product", Key, "Name"), withFieldAuthorizer: false);

        reader.Last!.EntityName.Should().Be(nameof(Product));
        reader.Last.EntityKey.Should().Be(Key);
        reader.Last.PropertyName.Should().Be("Name");
        reader.Last.Take.Should().Be(20);
    }

    [Fact]
    public async Task Without_field_security_only_unclassified_values_are_shown()
    {
        var (records, _) = await RunAsync(new ListEntityChanges("Shop.Product"), withFieldAuthorizer: false);

        var values = records.ToDictionary(r => r.Field, r => r.NewValue);
        values["Name"].Should().Be("Bolt M6");
        values["Cost"].Should().BeNull("a [Classified] field needs clearance");
        values["Token"].Should().BeNull("secrets never leave");
        values["Buyer"].Should().BeNull("personal data needs a field authorizer");
        values["Removed"].Should().BeNull("an unknown property fails closed");
        records.Should().OnlyContain(r => r.ChangedAt.Offset == TimeSpan.Zero && r.ResourceId == Key);
    }

    [Fact]
    public async Task With_field_security_the_mask_decides_but_secrets_stay_hidden()
    {
        var (records, _) = await RunAsync(new ListEntityChanges("Shop.Product"), withFieldAuthorizer: true);

        var values = records.ToDictionary(r => r.Field, r => r.OldValue);
        values["Name"].Should().Be("Bolt");
        values["Cost"].Should().BeNull("an anonymous caller has no clearance for a confidential field");
        values["Buyer"].Should().Be("ann");
        values["Token"].Should().BeNull();
    }

    [Theory]
    [InlineData("Shop.Unknown")]
    [InlineData("Sales.Nothing")]
    public async Task An_unknown_resource_type_is_refused(string resourceType)
    {
        var run = () => RunAsync(new ListEntityChanges(resourceType), withFieldAuthorizer: false);

        await run.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_page_over_100_is_refused()
    {
        var run = () => RunAsync(new ListEntityChanges("Shop.Product", Take: 101), withFieldAuthorizer: false);

        await run.Should().ThrowAsync<ArgumentException>();
    }
}
