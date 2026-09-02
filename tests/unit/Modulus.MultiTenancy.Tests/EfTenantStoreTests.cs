using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using FluentAssertions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

[Trait("Category", "Unit")]
public sealed class EfTenantStoreTests : IDisposable
{
    // A kept-open in-memory SQLite connection: the schema lives as long as the
    // connection, giving a real relational store (unique index, SQL translation)
    // without a file or container.
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EfTenantStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite(_connection));

        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>()
            .Database.EnsureCreated();
    }

    [Fact]
    public void AddEfCoreTenantStore_SupersedesNullStore()
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantStore>()
            .Should().BeOfType<ScopedTenantStoreBridge>();
    }

    [Fact]
    public async Task FindBySlug_ReturnsActiveTenant()
    {
        var created = await WithManager(m => m.CreateAsync("acme", "Acme Inc"));

        var found = await WithStore(s => s.FindBySlugAsync("acme", default));

        found.Should().NotBeNull();
        found!.TenantId.Should().Be(created.TenantId);
        found.TenantSlug.Should().Be("acme");
        found.DisplayName.Should().Be("Acme Inc");
    }

    [Fact]
    public async Task FindById_ReturnsActiveTenant()
    {
        var created = await WithManager(m => m.CreateAsync("globex"));

        var found = await WithStore(s => s.FindByIdAsync(created.TenantId, default));

        found.Should().NotBeNull();
        found!.TenantSlug.Should().Be("globex");
    }

    [Fact]
    public async Task Find_UnknownTenant_ReturnsNull()
    {
        (await WithStore(s => s.FindBySlugAsync("nope", default))).Should().BeNull();
        (await WithStore(s => s.FindByIdAsync(Guid.NewGuid(), default))).Should().BeNull();
    }

    [Fact]
    public async Task DeactivatedTenant_FailsClosed_ResolvesToNull()
    {
        var created = await WithManager(m => m.CreateAsync("initech"));

        // Active → resolves.
        (await WithStore(s => s.FindBySlugAsync("initech", default)))
            .Should().NotBeNull();

        await WithManager(m => m.SetActiveAsync(created.TenantId, isActive: false));

        // Deactivated → the store behaves as if the tenant does not exist.
        (await WithStore(s => s.FindBySlugAsync("initech", default)))
            .Should().BeNull();
        (await WithStore(s => s.FindByIdAsync(created.TenantId, default)))
            .Should().BeNull();
    }

    [Fact]
    public async Task Create_DuplicateSlug_Throws()
    {
        await WithManager(m => m.CreateAsync("dup"));

        var act = () => WithManager(m => m.CreateAsync("dup"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task SetActive_UnknownTenant_ReturnsFalse()
    {
        var ok = await WithManager(m => m.SetActiveAsync(Guid.NewGuid(), true));
        ok.Should().BeFalse();
    }

    // ── Scope helpers ─────────────────────────────────────────────
    private async Task<T> WithStore<T>(Func<ITenantStore, Task<T>> act)
    {
        using var scope = _provider.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<ITenantStore>());
    }

    private async Task<T> WithManager<T>(Func<TenantManager, Task<T>> act)
    {
        using var scope = _provider.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<TenantManager>());
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
