namespace Modulus.AI.Connector.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AI.Connector.Capabilities;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Ai;
using Modulus.Mediator.Abstractions;
using Xunit;

[AiCapability("Test.Bad.Thing.Create", "Creates a thing.")]
public sealed record CreateThing : ICommand<Guid>;

[AiCapability("lowercase", "Badly named.")]
public sealed record BadlyNamed : IQuery<int>;

[AiCapability("Test.Catalog.Product.Search", "Same name as the real one.")]
public sealed record DuplicateName : IQuery<int>;

[AiCapability("Test.Orphan.Thing.List", "Lists things of an undeclared type.", ResourceType = "Orphan.Thing")]
public sealed record OrphanResourceType : IQuery<IReadOnlyList<ProductDto>>;

[AiResource("Test.Thing", "A thing without an id constructor.")]
public sealed record NoIdLookup(Guid A, Guid B) : IQuery<ProductDto>;

[AiResource("Test.Thing", "A thing.", TitleField = "Missing")]
public sealed record MissingTitleField(Guid Id) : IQuery<ProductDto>;

public sealed record BatchWithWrongConstructor(Guid Id) : IQuery<IReadOnlyList<ProductDto>>;

public sealed record BatchOfAnotherType(IReadOnlyCollection<Guid> Ids) : IQuery<IReadOnlyList<string>>;

[AiResource("Test.WrongBatch", "A thing whose batch lookup takes one id.", BatchLookup = typeof(BatchWithWrongConstructor))]
public sealed record WrongBatchLookup(Guid Id) : IQuery<ProductDto>;

[AiResource("Test.OtherBatch", "A thing whose batch lookup returns another type.", BatchLookup = typeof(BatchOfAnotherType))]
public sealed record OtherBatchLookup(Guid Id) : IQuery<ProductDto>;

[AiCapability("Test.Not.A.Request", "Not a mediator request.")]
public sealed class NotARequest;

[Trait("Category", "Unit")]
public sealed class RegistryAndRevocationTests
{
    private static readonly ModulusAiConnectorOptions Options = new();

    private static AiCapabilityRegistry Build(params Type[] types) => AiCapabilityRegistry.Build(types, Options);

    [Fact]
    public void Annotated_queries_become_capabilities_and_unannotated_types_are_ignored()
    {
        var registry = Build(typeof(SearchProducts), typeof(GetProduct), typeof(ProductDto), typeof(string));

        registry.Capabilities.Select(c => c.Name).Should().Equal("Test.Catalog.Product.Search");
        registry.Resources.Select(r => r.ResourceType).Should().Equal("Catalog.Product");
        registry.TryGetCapability("Test.Catalog.Product.Search", out var search).Should().BeTrue();
        search.RequiredPermission.Should().Be("catalog:read");
        search.ItemType.Should().Be<ProductDto>();
    }

    [Theory]
    [InlineData(typeof(CreateThing), "command")]
    [InlineData(typeof(BadlyNamed), "App.Module.Entity.Verb")]
    [InlineData(typeof(OrphanResourceType), "Orphan.Thing")]
    [InlineData(typeof(NoIdLookup), "constructor")]
    [InlineData(typeof(MissingTitleField), "Missing")]
    [InlineData(typeof(NotARequest), "IQuery")]
    [InlineData(typeof(WrongBatchLookup), "taking the ids")]
    [InlineData(typeof(OtherBatchLookup), "same record type")]
    public void Malformed_declarations_fail_at_startup(Type type, string mentions)
    {
        var act = () => Build(typeof(SearchProducts), typeof(GetProduct), type);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{mentions}*");
    }

    [Fact]
    public void A_duplicate_name_fails()
    {
        var act = () => Build(typeof(SearchProducts), typeof(GetProduct), typeof(DuplicateName));

        act.Should().Throw<InvalidOperationException>().WithMessage("*twice*");
    }

    [Fact]
    public void A_description_over_the_limit_fails()
    {
        var act = () => AiCapabilityRegistry.Build([typeof(SearchProducts), typeof(GetProduct)], new ModulusAiConnectorOptions { MaxDescriptionLength = 5 });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Settings_without_keys_or_instances_are_refused()
    {
        var result = new AiConnectorOptionsValidator().Validate(null, new ModulusAiConnectorOptions());

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ApiKeyHashes").And.Contain("Instances").And.Contain("Issuer").And.Contain("BaseUrl");
    }

    [Fact]
    public void A_raw_key_instead_of_a_hash_is_refused()
    {
        var options = new ModulusAiConnectorOptions { ApiKeyHashes = ["my-plain-key"] };

        new AiConnectorOptionsValidator().Validate(null, options).FailureMessage.Should().Contain("never the keys");
    }

    [Fact]
    public async Task An_access_change_is_signalled_to_the_platform_until_it_acknowledges()
    {
        // The first instance reads another company, so only the company instance is signalled.
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:Instances:0:TenantId"] = Guid.NewGuid().ToString());
        host.Platform.Enqueue(HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetServices<IAccessChangeObserver>().NotifyAccessChangedAsync(
                new AccessChange { Kind = AccessChangeKinds.Membership, Reason = "membership.removed", TenantId = TestTenantRestorer.Company, UserId = TestUsers.Reader });
        }

        (await host.Platform.Succeeded.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        var received = host.Platform.Received.ToList();
        received.Should().HaveCount(3, "two failures, then the acknowledged attempt");
        received.Should().OnlyContain(r => r.Path == "/revocations/scope" && r.Authorization == "ApiKey platform-key");
        received.Select(r => r.Body).Distinct().Should().ContainSingle("every retry sends the same payload");

        var signal = JsonDocument.Parse(received[0].Body).RootElement;
        signal.GetProperty("revocationKey").GetString().Should().Be($"modulus:{ConnectorTestHost.CompanyInstance}");
        signal.GetProperty("reason").GetString().Should().Be("membership.removed");
        host.Services.GetRequiredService<Revocation.AiRevocationQueue>().Count.Should().Be(0);
    }

    [Fact]
    public async Task A_host_wide_change_signals_every_instance()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetServices<IAccessChangeObserver>().NotifyAccessChangedAsync(
                new AccessChange { Kind = AccessChangeKinds.Grant, Reason = "permissiongrant.granted" });
        }

        (await host.Platform.Succeeded.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        (await host.Platform.Succeeded.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        host.Platform.Received.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("appInstanceId").GetString())
            .Should().BeEquivalentTo([ConnectorTestHost.Instance, ConnectorTestHost.CompanyInstance]);
    }
}
