namespace Modulus.Data.MongoDB.Tests;

using FluentAssertions;
using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Data.MongoDB.Extensions;
using NSubstitute;
using Testcontainers.MongoDb;
using Xunit;
using Invoice = TenantScopedCollectionTests.Invoice;

/// <summary>Database per tenant for MongoDB: the ambient tenant picks the database, no tenant never falls back.</summary>
[Trait("Category", "Unit")]
public sealed class TenantMongoDatabaseTests
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private ServiceProvider Provider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tenant);
        register(services);
        return services.BuildServiceProvider();
    }

    private static Action<MongoOptions> Options => o =>
    {
        o.ConnectionString = "mongodb://localhost:27017";
        o.DatabaseName = "shop";
    };

    [Fact]
    public void A_tenant_gets_its_own_database_and_the_host_the_host_database()
    {
        using var sp = Provider(s => s.AddMongoDatabasePerTenant(Options, id => $"shop_{id:N}"));

        _tenant.TenantId.Returns(TenantA);
        using (var scope = sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<ITenantMongoDatabase>().Database.DatabaseNamespace.DatabaseName
                .Should().Be($"shop_{TenantA:N}");

        _tenant.TenantId.Returns((Guid?)null);
        _tenant.IsHost.Returns(true);
        using (var scope = sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<ITenantMongoDatabase>().Database.DatabaseNamespace.DatabaseName
                .Should().Be("shop");

        sp.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName.Should().Be("shop", "infrastructure stays in the host database");
    }

    [Fact]
    public void No_tenant_in_scope_throws_instead_of_falling_back()
    {
        using var sp = Provider(s => s.AddMongoDatabasePerTenant(Options, id => $"shop_{id:N}"));
        using var scope = sp.CreateScope();

        scope.ServiceProvider.Invoking(p => p.GetRequiredService<ITenantMongoDatabase>())
            .Should().Throw<InvalidOperationException>().WithMessage("*no tenant is in scope*");
    }

    [Fact]
    public void A_tenant_database_may_not_be_the_host_database()
    {
        _tenant.TenantId.Returns(TenantA);
        using var sp = Provider(s => s.AddMongoDatabasePerTenant(Options, _ => "shop"));
        using var scope = sp.CreateScope();

        scope.ServiceProvider.Invoking(p => p.GetRequiredService<ITenantMongoDatabase>())
            .Should().Throw<InvalidOperationException>().WithMessage("*differ from the host database*");
    }

    [Fact]
    public void The_shared_registration_hands_every_tenant_the_one_database()
    {
        _tenant.TenantId.Returns(TenantA);
        using var sp = Provider(s => s.AddMongoDatabase(Options));
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantMongoDatabase>().Database
            .Should().BeSameAs(sp.GetRequiredService<IMongoDatabase>());
    }
}

/// <summary>Each company's documents live in its own database on a real MongoDB (needs Docker).</summary>
[Trait("Category", "Integration")]
public sealed class TenantMongoDatabaseIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7").Build();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var services = new ServiceCollection();
        services.AddSingleton(_tenant);
        services.AddMongoDatabasePerTenant(
            o =>
            {
                o.ConnectionString = _container.GetConnectionString();
                o.DatabaseName = "shop";
            },
            id => $"shop_{id:N}");
        services.AddScoped<InvoiceContext>();
        _sp = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Each_company_writes_and_reads_only_its_own_database()
    {
        await InTenant(TenantA, c => c.Invoices.InsertOneAsync(new Invoice { Number = "a1" }));
        await InTenant(TenantB, c => c.Invoices.InsertOneAsync(new Invoice { Number = "b1" }));

        var client = _sp.GetRequiredService<IMongoClient>();
        (await client.GetDatabase($"shop_{TenantA:N}").GetCollection<Invoice>("invoices").Find(_ => true).ToListAsync())
            .Select(i => i.Number).Should().Equal("a1");
        (await client.GetDatabase($"shop_{TenantB:N}").GetCollection<Invoice>("invoices").Find(_ => true).ToListAsync())
            .Select(i => i.Number).Should().Equal("b1");
        (await (await client.GetDatabase("shop").ListCollectionNamesAsync()).ToListAsync()).Should().NotContain("invoices");
    }

    private async Task InTenant(Guid tenantId, Func<InvoiceContext, Task> work)
    {
        _tenant.TenantId.Returns(tenantId);
        using var scope = _sp.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<InvoiceContext>());
    }

    private sealed class InvoiceContext(ITenantMongoDatabase database, IOptions<MongoOptions> options, ICurrentTenant tenant)
        : ModuleMongoContext(database, options, tenant)
    {
        public TenantScopedCollection<Invoice> Invoices => GetTenantCollection<Invoice>("invoices");
    }
}
