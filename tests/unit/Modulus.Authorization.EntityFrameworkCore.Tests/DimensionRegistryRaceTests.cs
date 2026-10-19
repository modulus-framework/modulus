using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Dimensions;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// Two registrations of the same new code race: the one that saves second hits the unique index. It must return the value the
/// winner registered, not fail. The race is reproduced by a rival that writes the winning row just before the loser saves.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DimensionRegistryRaceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant>(new CompanyTenant(Guid.Parse("c0c0c0c0-0000-4000-8000-000000000001")));
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection).AddInterceptors(new RivalRegistration()));
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
    public async Task A_registration_that_loses_the_race_returns_the_winners_value()
    {
        var registry = _provider.GetRequiredService<IDimensionValueRegistry>();

        var value = await registry.RegisterAsync("customer", "CUST-1", "Loser's label");

        value.Label.Should().Be(RivalRegistration.WinnerLabel, "the value the winner registered is the one the code now has");
        var listed = await registry.ListAsync("customer");
        listed.Should().ContainSingle().Which.Id.Should().Be(value.Id);
    }

    // Writes the winning row through the same connection, just before the loser's first save. Only the first save is raced.
    private sealed class RivalRegistration : SaveChangesInterceptor
    {
        public const string WinnerLabel = "Winner's label";
        private bool _raced;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Race(eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Race(eventData);
            return ValueTask.FromResult(result);
        }

        private void Race(DbContextEventData eventData)
        {
            if (_raced || eventData.Context is null)
                return;
            var added = eventData.Context.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added && e.Entity.GetType().Name == "DimensionValueRow")
                .ToList();
            if (added.Count == 0)
                return;

            _raced = true;
            var row = added.Single();
            eventData.Context.Database.ExecuteSqlRaw(
                "INSERT INTO ModulusDimensionValues (TenantId, Id, Dimension, Code, Label) VALUES ({0}, {1}, {2}, {3}, {4})",
                row.Property("TenantId").CurrentValue!, Guid.NewGuid(), row.Property("Dimension").CurrentValue!,
                row.Property("Code").CurrentValue!, WinnerLabel);
        }
    }

    private sealed class CompanyTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid? TenantId { get; } = tenantId;
        public string? TenantSlug => null;
        public bool IsAvailable => true;
        public bool IsHost => false;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
