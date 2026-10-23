using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Settings, phase 5: typed and restricted definitions, host pins, the source of an effective value, the administrator's view and
/// writes, and the cache version key.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsPhase5Tests
{
    private static (IServiceProvider Services, CurrentTenant Tenant) Build(Dictionary<string, string?>? config = null, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser>(_ => new TestUser());
        services.AddMemoryCache();
        if (config is null)
            services.AddModulusSettings();
        else
            services.AddModulusSettings(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        extra?.Invoke(services);
        var provider = services.BuildServiceProvider();
        return (provider, (CurrentTenant)provider.GetRequiredService<ICurrentTenant>());
    }

    [Theory]
    [InlineData(SettingValueType.Int32, "12", true)]
    [InlineData(SettingValueType.Int32, "twelve", false)]
    [InlineData(SettingValueType.Int64, "9000000000", true)]
    [InlineData(SettingValueType.Decimal, "12.50", true)]
    [InlineData(SettingValueType.Decimal, "twelve", false)]
    [InlineData(SettingValueType.Boolean, "false", true)]
    [InlineData(SettingValueType.Boolean, "maybe", false)]
    [InlineData(SettingValueType.Guid, "5f3c0a4e-3b0e-4b8e-9d4a-2f1c6a7b8c9d", true)]
    [InlineData(SettingValueType.Guid, "not-a-guid", false)]
    [InlineData(SettingValueType.TimeSpan, "01:30:00", true)]
    [InlineData(SettingValueType.TimeSpan, "soon", false)]
    public async Task Write_IsValidatedAgainstTheDefinitionType(SettingValueType type, string value, bool accepted)
    {
        var (services, _) = Build();
        services.GetRequiredService<ISettingDefinitionRegistry>().Add(new SettingDefinition("Shop.Limit") { ValueType = type });
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        var write = () => manager.SetAsync("Shop.Limit", value, SettingScope.Global);

        if (accepted)
            await write.Should().NotThrowAsync();
        else
            await write.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Write_WithAllowedValues_AcceptsOnlyThoseValues_IgnoringCase()
    {
        var (services, _) = Build();
        services.GetRequiredService<ISettingDefinitionRegistry>()
            .Add(new SettingDefinition("Shop.Tender") { AllowedValues = ["cash", "card"] });
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        await manager.SetAsync("Shop.Tender", "CARD", SettingScope.Global);
        await FluentActions.Awaiting(() => manager.SetAsync("Shop.Tender", "voucher", SettingScope.Global))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Write_AtAScopeTheDefinitionDoesNotAllow_IsRefused()
    {
        var (services, tenant) = Build();
        services.GetRequiredService<ISettingDefinitionRegistry>()
            .Add(new SettingDefinition("Shop.Region") { AllowedScopes = SettingScopes.Global });
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
        {
            await FluentActions.Awaiting(() => manager.SetAsync("Shop.Region", "EU", SettingScope.Tenant))
                .Should().ThrowAsync<ValidationException>();
            await manager.SetAsync("Shop.Region", "EU", SettingScope.Global);
        }
    }

    [Fact]
    public async Task Write_AtTenantScope_WithNoCompanyInScope_IsRefused_AndNeverWritesTheInstallation()
    {
        var (services, _) = Build();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        await FluentActions.Awaiting(() => manager.SetAsync("Shop.Currency", "EUR", SettingScope.Tenant))
            .Should().ThrowAsync<ValidationException>();
        (await manager.GetOrNullAsync("Shop.Currency")).Should().BeNull("the installation-wide value must not have changed");
    }

    [Fact]
    public async Task Write_OverLongValue_IsRefused()
    {
        var (services, _) = Build();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        await FluentActions.Awaiting(() => manager.SetAsync("Shop.Note", new string('x', 4001), SettingScope.Global))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Pin_WinsOverEveryScope_AndRefusesWrites()
    {
        var (services, tenant) = Build(new Dictionary<string, string?> { ["Settings:Pinned:Shop.Currency"] = "USD" });
        var userId = Guid.NewGuid();
        TestUser.CurrentId = userId;
        try
        {
            using var scope = services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

            using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
            {
                await FluentActions.Awaiting(() => manager.SetAsync("Shop.Currency", "EUR", SettingScope.Tenant))
                    .Should().ThrowAsync<ConflictException>();

                (await manager.GetOrNullAsync("Shop.Currency")).Should().Be("USD");
                var resolution = await manager.ResolveAsync("Shop.Currency");
                resolution.Should().BeEquivalentTo(new SettingResolution("Shop.Currency", "USD", SettingSource.Pinned));
            }
        }
        finally
        {
            TestUser.CurrentId = null;
        }
    }

    [Fact]
    public async Task Resolve_ReportsTheScopeTheValueCameFrom()
    {
        var (services, tenant) = Build();
        services.GetRequiredService<ISettingDefinitionRegistry>().Add(new SettingDefinition("Shop.Theme", DefaultValue: "light"));
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        TestUser.CurrentId = userId;
        try
        {
            using (tenant.Change(new TenantInfo(tenantId, "acme")))
            {
                (await manager.ResolveAsync("Shop.Theme")).Source.Should().Be(SettingSource.Default);

                await manager.SetAsync("Shop.Theme", "dark", SettingScope.Global);
                (await manager.ResolveAsync("Shop.Theme")).Source.Should().Be(SettingSource.Global);

                await manager.SetAsync("Shop.Theme", "contrast", SettingScope.Tenant);
                (await manager.ResolveAsync("Shop.Theme")).Source.Should().Be(SettingSource.Tenant);

                await manager.SetAsync("Shop.Theme", "mono", SettingScope.User);
                (await manager.ResolveAsync("Shop.Theme")).Should().BeEquivalentTo(new SettingResolution("Shop.Theme", "mono", SettingSource.User));
            }

            (await manager.ResolveAsync("Shop.Unknown")).Should().BeEquivalentTo(new SettingResolution("Shop.Unknown", null, SettingSource.None));
        }
        finally
        {
            TestUser.CurrentId = null;
        }
    }

    [Fact]
    public async Task Administration_ListsEveryDefinition_WithTheValueThatApplies()
    {
        var (services, _) = Build(new Dictionary<string, string?> { ["Settings:Pinned:Shop.Locked"] = "yes" });
        var registry = services.GetRequiredService<ISettingDefinitionRegistry>();
        registry.Add(new SettingDefinition("Shop.Locked", DefaultValue: "no", DisplayName: "Locked"));
        registry.Add(new SettingDefinition("Shop.Tender") { AllowedValues = ["cash", "card"] });
        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        await admin.SetAsync("Shop.Tender", "card", SettingScope.Global);
        var views = await admin.ListAsync();

        views.Select(v => v.Name).Should().Equal("Shop.Locked", "Shop.Tender");
        var locked = views[0];
        locked.IsPinned.Should().BeTrue();
        locked.EffectiveValue.Should().Be("yes");
        locked.Source.Should().Be(SettingSource.Pinned);
        locked.DisplayName.Should().Be("Locked");
        var tender = views[1];
        tender.EffectiveValue.Should().Be("card");
        tender.Source.Should().Be(SettingSource.Global);
        tender.AllowedValues.Should().Equal("cash", "card");
    }

    [Fact]
    public async Task Administration_RefusesNamesWithNoDefinition()
    {
        var (services, _) = Build();
        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        await FluentActions.Awaiting(() => admin.SetAsync("Shop.Typo", "x", SettingScope.Global)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => admin.RemoveAsync("Shop.Typo", SettingScope.Global)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Cache_ServesRepeatedReads_AndAWriteMakesThemStaleAtOnce()
    {
        var inner = new CountingStore();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var version = new SettingCacheVersion();
        var store = new CachedSettingStore(inner, cache, version, TimeSpan.FromMinutes(5));

        (await store.GetOrNullAsync("Shop.Theme", null, null)).Should().BeNull();
        (await store.GetOrNullAsync("Shop.Theme", null, null)).Should().BeNull();
        inner.Reads.Should().Be(1, "a miss is cached too");

        await store.SetAsync("Shop.Theme", "dark", null, null);
        (await store.GetOrNullAsync("Shop.Theme", null, null)).Should().Be("dark");
        inner.Reads.Should().Be(2, "the write advanced the version, so the read went to the store once");
    }

    [Fact]
    public async Task Cache_TellsTheOtherNodes_AfterEachWriteAndRemove()
    {
        // Each write advances this node's version and then announces the change, so other nodes can drop their copies.
        var inner = new CountingStore();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var version = new SettingCacheVersion();
        var notifier = new CountingNotifier();
        var store = new CachedSettingStore(inner, cache, version, TimeSpan.FromMinutes(5), notifier);

        await store.SetAsync("Shop.Theme", "dark", null, null);
        await store.RemoveAsync("Shop.Theme", null, null);

        notifier.Announcements.Should().Be(2, "one announcement per committed write and per removal");
    }

    [Fact]
    public async Task Cache_StillWrites_WhenNoOneIsListening()
    {
        var store = new CachedSettingStore(new CountingStore(), new MemoryCache(new MemoryCacheOptions()), new SettingCacheVersion(), TimeSpan.FromMinutes(5));

        await FluentActions.Awaiting(() => store.SetAsync("Shop.Theme", "dark", null, null)).Should().NotThrowAsync();
    }

    private sealed class CountingNotifier : ISettingChangeNotifier
    {
        public int Announcements { get; private set; }

        public Task NotifyChangedAsync(CancellationToken ct = default)
        {
            Announcements++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Options_BindPins_FromTheSettingsSection()
    {
        var (services, _) = Build(new Dictionary<string, string?> { ["Settings:Pinned:Shop.Currency"] = "USD", ["Settings:CacheDuration"] = "00:00:10" });

        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ModulusSettingsOptions>>().Value;

        options.Pinned.Should().ContainKey("Shop.Currency").WhoseValue.Should().Be("USD");
        options.CacheDuration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Pins_AreHonoured_WhenTheSettingsAreRegisteredWithoutTheConfiguration()
    {
        // A host that calls AddModulusSettings() without the configuration still gets the Settings section from its container.
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser>(_ => new TestUser());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:Pinned:Shop.Currency"] = "USD" }).Build());
        services.AddModulusSettings();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<ISettingManager>().GetOrNullAsync("Shop.Currency")).Should().Be("USD");
    }

    private sealed class CountingStore : ISettingStore
    {
        private readonly Dictionary<(string, Guid?, Guid?), string> _values = [];

        public int Reads { get; private set; }

        public Task<string?> GetOrNullAsync(string name, Guid? tenantId, Guid? userId, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(_values.TryGetValue((name, tenantId, userId), out var value) ? value : null);
        }

        public Task SetAsync(string name, string? value, Guid? tenantId, Guid? userId, CancellationToken ct = default)
        {
            _values[(name, tenantId, userId)] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string name, Guid? tenantId, Guid? userId, CancellationToken ct = default)
        {
            _values.Remove((name, tenantId, userId));
            return Task.CompletedTask;
        }
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
