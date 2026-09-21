using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Spec for the setting system: definition defaults, user → tenant → global
/// precedence, typed reads, and tenant isolation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsTests
{
    private static (IServiceProvider Services, CurrentTenant Tenant) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser>(_ => new TestUser());
        services.AddModulusSettings();
        var provider = services.BuildServiceProvider();
        return (provider, (CurrentTenant)provider.GetRequiredService<ICurrentTenant>());
    }

    [Fact]
    public async Task Get_UnknownSetting_ReturnsFallback()
    {
        var (services, _) = BuildServices();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        (await manager.GetAsync<int>("Shop.MaxItems", 42)).Should().Be(42);
        (await manager.GetOrNullAsync("Shop.MaxItems")).Should().BeNull();
    }

    [Fact]
    public async Task Get_FallsBackToDefinitionDefault()
    {
        var (services, _) = BuildServices();
        services.GetRequiredService<ISettingDefinitionRegistry>()
            .Add(new SettingDefinition("Shop.Currency", DefaultValue: "USD"));
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        (await manager.GetOrNullAsync("Shop.Currency")).Should().Be("USD");
    }

    [Fact]
    public async Task Get_ResolvesUserOverTenantOverGlobal()
    {
        var (services, tenant) = BuildServices();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        TestUser.CurrentId = userId;
        try
        {
            using (tenant.Change(new TenantInfo(tenantId, "acme")))
            {
                await manager.SetAsync("Shop.Currency", "G", SettingScope.Global);
                (await manager.GetOrNullAsync("Shop.Currency")).Should().Be("G");

                await manager.SetAsync("Shop.Currency", "T", SettingScope.Tenant);
                (await manager.GetOrNullAsync("Shop.Currency")).Should().Be("T");

                await manager.SetAsync("Shop.Currency", "U", SettingScope.User);
                (await manager.GetOrNullAsync("Shop.Currency")).Should().Be("U");

                await manager.RemoveAsync("Shop.Currency", SettingScope.User);
                (await manager.GetOrNullAsync("Shop.Currency")).Should().Be("T");
            }
        }
        finally
        {
            TestUser.CurrentId = null;
        }
    }

    [Fact]
    public async Task Get_TenantValues_AreIsolated()
    {
        var (services, tenant) = BuildServices();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        using (tenant.Change(new TenantInfo(tenantA, "a")))
            await manager.SetAsync("Shop.Currency", "AAA", SettingScope.Tenant);

        using (tenant.Change(new TenantInfo(tenantB, "b")))
            (await manager.GetOrNullAsync("Shop.Currency")).Should().BeNull();
    }

    [Fact]
    public async Task Get_Typed_ReadsPrimitivesAndEnums()
    {
        var (services, _) = BuildServices();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        await manager.SetAsync("A.Int", "7", SettingScope.Global);
        await manager.SetAsync("A.Bool", "true", SettingScope.Global);
        await manager.SetAsync("A.Scope", "Tenant", SettingScope.Global);
        await manager.SetAsync("A.Broken", "not-a-number", SettingScope.Global);

        (await manager.GetAsync<int>("A.Int", 0)).Should().Be(7);
        (await manager.GetAsync<bool>("A.Bool", false)).Should().BeTrue();
        (await manager.GetAsync<SettingScope>("A.Scope", SettingScope.Global)).Should().Be(SettingScope.Tenant);
        (await manager.GetAsync<int>("A.Broken", 99)).Should().Be(99, "unparseable values fall back instead of throwing");
    }

    private sealed class TestUser : ICurrentUser
    {
        public static Guid? CurrentId { get; set; }

        public Guid? UserId => CurrentId;
        public string? UserName => CurrentId.HasValue ? "test-user" : null;
        public string? Email => null;
        public bool IsAuthenticated => CurrentId.HasValue;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
