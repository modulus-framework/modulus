using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Dimensions;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// Typed dimension values: a business code (a warehouse code) is registered once per company and resolves to the id that scope
/// assignments carry. Codes are canonical (trimmed, upper-case), and a company never sees another company's codes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DimensionValueRegistryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly MutableTenant _tenant = new();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContextBase>>()
            .CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Registering_the_same_code_in_another_spelling_returns_the_same_value()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();

        var first = await registry.RegisterAsync("warehouse", "WH-01", "Main warehouse");
        var again = await registry.RegisterAsync("Warehouse", "  wh-01 ", "ignored label");

        again.Id.Should().Be(first.Id);
        again.Code.Should().Be("WH-01");
        again.Label.Should().Be("Main warehouse", "the first registration keeps its label");
    }

    [Fact]
    public async Task A_code_resolves_to_its_id_and_an_unknown_code_resolves_to_nothing()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        var registered = await registry.RegisterAsync("warehouse", "WH-01", "Main");

        (await registry.ResolveIdAsync("warehouse", "wh-01")).Should().Be(registered.Id);
        (await registry.ResolveIdAsync("warehouse", "WH-99")).Should().BeNull();
    }

    [Fact]
    public async Task The_same_code_in_another_dimension_is_a_different_value()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();

        var warehouse = await registry.RegisterAsync("warehouse", "NORTH", "North warehouse");
        var region = await registry.RegisterAsync("region", "NORTH", "North region");

        region.Id.Should().NotBe(warehouse.Id);
    }

    [Fact]
    public async Task A_company_never_resolves_another_companys_codes()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        var companyAValue = await registry.RegisterAsync("warehouse", "WH-01", "A's warehouse");

        _tenant.TenantId = CompanyB;

        (await registry.ResolveIdAsync("warehouse", "WH-01")).Should().BeNull();
        (await registry.ListAsync("warehouse")).Should().BeEmpty();

        var companyBValue = await registry.RegisterAsync("warehouse", "WH-01", "B's warehouse");
        companyBValue.Id.Should().NotBe(companyAValue.Id, "the same code is a separate value in another company");
    }

    [Fact]
    public async Task Listing_returns_one_dimensions_values_by_code()
    {
        _tenant.TenantId = CompanyA;
        var registry = Registry();
        await registry.RegisterAsync("warehouse", "WH-02", "Second");
        await registry.RegisterAsync("warehouse", "WH-01", "First");
        await registry.RegisterAsync("region", "NORTH", "North");

        var values = await registry.ListAsync("warehouse");

        values.Select(v => v.Code).Should().Equal("WH-01", "WH-02");
    }

    [Fact]
    public async Task A_blank_label_defaults_to_the_code()
    {
        _tenant.TenantId = CompanyA;

        var value = await Registry().RegisterAsync("warehouse", "WH-07", "  ");

        value.Label.Should().Be("WH-07");
    }

    [Theory]
    [InlineData("Warehouse Code!")]
    [InlineData("9warehouse")]
    [InlineData("")]
    public async Task An_invalid_dimension_name_is_refused(string dimension)
    {
        _tenant.TenantId = CompanyA;

        var act = () => Registry().RegisterAsync(dimension, "WH-01", "x");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task An_empty_code_is_refused()
    {
        _tenant.TenantId = CompanyA;

        var act = () => Registry().RegisterAsync("warehouse", "   ", "x");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private IDimensionValueRegistry Registry()
    {
        var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IDimensionValueRegistry>();
    }

    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
