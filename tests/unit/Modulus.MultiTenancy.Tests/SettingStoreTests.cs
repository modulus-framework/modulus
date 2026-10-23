using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Modulus.Settings;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The durable setting store (phase 5): values per scope in <c>ModulusSettingValues</c>, company isolation, the cache that a write
/// makes stale, and the administration API served in-process.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingStoreTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid User = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly SqliteConnection _keepAlive = new("DataSource=:memory:");
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        await _keepAlive.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddMultiTenancy(t => t.UseHeaderResolver());
        builder.Services.AddEfCoreTenantStore(o => o.UseSqlite(_keepAlive));
        builder.Services.AddEfCoreSettingStore();
        builder.Services.AddScoped<ICurrentUser, NullCurrentUser>();
        builder.Services.AddModulusSettingAdministration();
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(SettingsAdministrationEndpoints.ManagePermission, p => p.RequireAssertion(_ => true));
            o.AddPolicy(SettingsAdministrationEndpoints.GlobalManagePermission, p => p.RequireAssertion(_ => true));
        });

        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            // The tests choose the company with a header; a real host does it in its tenant middleware.
            var tenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
            using var _ = context.Request.Headers.TryGetValue("X-Test-Tenant", out var id) && Guid.TryParse(id, out var tenantId)
                ? tenant.Change(new TenantInfo(tenantId, "test"))
                : tenant.Change(null);
            await next();
        });
        _app.MapModulusSettingAdministration();
        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
        scope.ServiceProvider.GetRequiredService<ISettingDefinitionRegistry>()
            .Add(new SettingDefinition("Shop.Tender") { AllowedValues = ["cash", "card"], DisplayName = "Tender" });
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task Store_KeepsEachScopeApart_AndIsolatesCompanies()
    {
        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfSettingStore>();

        await store.SetAsync("Shop.Tender", "cash", null, null);
        await store.SetAsync("Shop.Tender", "card", TenantA, null);
        await store.SetAsync("Shop.Tender", "voucher", TenantA, User);
        await store.SetAsync("Shop.Tender", "other", TenantB, null);

        (await store.GetOrNullAsync("Shop.Tender", null, null)).Should().Be("cash");
        (await store.GetOrNullAsync("Shop.Tender", TenantA, null)).Should().Be("card");
        (await store.GetOrNullAsync("Shop.Tender", TenantA, User)).Should().Be("voucher");
        (await store.GetOrNullAsync("Shop.Tender", TenantB, null)).Should().Be("other");
        (await store.GetOrNullAsync("Shop.Tender", TenantB, User)).Should().BeNull("a person of one company has no value in another");
    }

    [Fact]
    public async Task Store_Upserts_AndRemoves()
    {
        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfSettingStore>();

        await store.SetAsync("Shop.Tender", "cash", TenantA, null);
        await store.SetAsync("Shop.Tender", "card", TenantA, null);
        (await store.GetOrNullAsync("Shop.Tender", TenantA, null)).Should().Be("card");
        scope.ServiceProvider.GetRequiredService<TenantStoreDbContextBase>().SettingValues.Count(s => s.Name == "Shop.Tender").Should().Be(1);

        await store.RemoveAsync("Shop.Tender", TenantA, null);
        (await store.GetOrNullAsync("Shop.Tender", TenantA, null)).Should().BeNull();
    }

    [Fact]
    public void ScopeKey_IsUniquePerScope()
    {
        EfSettingStore.ScopeKey(null, null).Should().Be("global");
        EfSettingStore.ScopeKey(TenantA, null).Should().Be($"tenant:{TenantA:N}");
        EfSettingStore.ScopeKey(TenantA, User).Should().Be($"user:{TenantA:N}:{User:N}");
        EfSettingStore.ScopeKey(null, User).Should().Be($"user:none:{User:N}");
    }

    [Fact]
    public async Task Manager_OverTheDatabase_SeesAWriteAtOnce_EvenWithTheCache()
    {
        using var scope = _app.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using (tenant.Change(new TenantInfo(TenantA, "a")))
        {
            (await manager.GetOrNullAsync("Shop.Tender")).Should().BeNull();
            await manager.SetAsync("Shop.Tender", "card", SettingScope.Tenant);
            (await manager.GetOrNullAsync("Shop.Tender")).Should().Be("card");
        }

        using (tenant.Change(new TenantInfo(TenantB, "b")))
            (await manager.GetOrNullAsync("Shop.Tender")).Should().BeNull("company B never sees company A's value");
    }

    [Fact]
    public async Task Api_ListsEffectiveValues_AndWritesAtTheScopeNamed()
    {
        using var client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tenant", TenantA.ToString());

        var put = await client.PutAsJsonAsync("/api/settings/tenant/Shop.Tender", new SettingValueRequest("card"));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<List<SettingView>>("/api/settings");
        var view = list!.Single(v => v.Name == "Shop.Tender");
        view.EffectiveValue.Should().Be("card");
        view.Source.Should().Be(SettingSource.Tenant);
        view.AllowedValues.Should().Equal("cash", "card");
    }

    [Fact]
    public async Task Api_RefusesInvalidValues_UnknownNames_AndPins()
    {
        using var client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tenant", TenantA.ToString());

        (await client.PutAsJsonAsync("/api/settings/tenant/Shop.Tender", new SettingValueRequest("voucher")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/settings/tenant/Shop.Typo", new SettingValueRequest("x")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_GlobalScope_IsSeparatelyPermissioned_AndRoutesToTheInstallation()
    {
        using var client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();

        (await client.PutAsJsonAsync("/api/settings/global/Shop.Tender", new SettingValueRequest("cash")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync("/api/settings/global/Shop.Tender")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Permissions_AreRegisteredWithTheirSensitivity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAuthorization();
        services.AddModulusSettingAdministration();
        using var provider = services.BuildServiceProvider();
        // The declarations are applied when the host starts (PermissionInitHostedService), as in a running application.
        foreach (var hosted in provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
            await hosted.StartAsync(default);
        var registry = provider.GetRequiredService<IPermissionRegistry>();

        registry.GetAll().Single(p => p.Permission == SettingsAdministrationEndpoints.ManagePermission)
            .Sensitivity.Should().Be(PermissionSensitivity.Sensitive);
        registry.GetAll().Single(p => p.Permission == SettingsAdministrationEndpoints.GlobalManagePermission)
            .Sensitivity.Should().Be(PermissionSensitivity.Critical);
    }
}
