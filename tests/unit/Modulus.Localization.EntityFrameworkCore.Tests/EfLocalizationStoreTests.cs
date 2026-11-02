namespace Modulus.Localization.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>The database-backed localization store: set, get, remove, culture handling and isolation between resources.</summary>
[Trait("Category", "Unit")]
public sealed class EfLocalizationStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new($"DataSource=loc-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private ServiceProvider _root = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestLocalizationContext>(o => o.UseSqlite(_connection));
        services.AddModulusEfLocalizationStore<TestLocalizationContext>();
        _root = services.BuildServiceProvider();

        await using var db = await _root.GetRequiredService<IDbContextFactory<TestLocalizationContext>>().CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_stored_translation_is_read_back()
    {
        var store = _root.GetRequiredService<ILocalizationStore>();

        await store.SetAsync("Modulus.Identity", "en-US", "Login.Title", "Sign in");

        (await store.GetOrNullAsync("Modulus.Identity", "en-US", "Login.Title")).Should().Be("Sign in");
    }

    [Fact]
    public async Task Culture_is_case_insensitive()
    {
        var store = _root.GetRequiredService<ILocalizationStore>();

        await store.SetAsync("Shop", "fr-FR", "Hello", "Bonjour");

        (await store.GetOrNullAsync("Shop", "FR-fr", "Hello")).Should().Be("Bonjour");
    }

    [Fact]
    public async Task Setting_an_existing_key_replaces_its_value()
    {
        var store = _root.GetRequiredService<ILocalizationStore>();
        await store.SetAsync("Shop", "de-DE", "Save", "Speichern");

        await store.SetAsync("Shop", "de-DE", "Save", "Sichern");

        (await store.GetOrNullAsync("Shop", "de-DE", "Save")).Should().Be("Sichern");
    }

    [Fact]
    public async Task Setting_null_removes_the_translation()
    {
        var store = _root.GetRequiredService<ILocalizationStore>();
        await store.SetAsync("Shop", "es-ES", "Cancel", "Cancelar");

        await store.SetAsync("Shop", "es-ES", "Cancel", null);

        (await store.GetOrNullAsync("Shop", "es-ES", "Cancel")).Should().BeNull();
    }

    [Fact]
    public async Task Resources_do_not_share_keys()
    {
        var store = _root.GetRequiredService<ILocalizationStore>();

        await store.SetAsync("Identity", "en", "Title", "Identity title");

        (await store.GetOrNullAsync("Catalog", "en", "Title")).Should().BeNull();
    }

    private sealed class TestLocalizationContext(DbContextOptions<TestLocalizationContext> options)
        : ModulusLocalizationDbContext(options);
}
