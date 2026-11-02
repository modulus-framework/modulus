using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Application settings: the framework catalog validates URLs, emails and colours, a sensitive value never reaches an administrator's read,
/// and a module can contribute definitions through <see cref="ISettingDefinitionContributor"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ApplicationSettingsTests
{
    private static IServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser>(_ => new TestUser());
        services.AddMemoryCache();
        services.AddModulusApplicationSettings();
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(Names.SupportUrl, "https://support.example.com/help", true)]
    [InlineData(Names.SupportUrl, "http://example.com", true)]
    [InlineData(Names.SupportUrl, "javascript:alert(1)", false)]
    [InlineData(Names.SupportUrl, "ftp://example.com", false)]
    [InlineData(Names.SupportUrl, "https://user:pass@example.com", false)]
    [InlineData(Names.SupportUrl, "/relative/path", false)]
    [InlineData(Names.SupportEmail, "help@example.com", true)]
    [InlineData(Names.SupportEmail, "help@example", false)]
    [InlineData(Names.SupportEmail, "Help <help@example.com>", false)]
    [InlineData(Names.PrimaryColor, "#1a2B3c", true)]
    [InlineData(Names.PrimaryColor, "#abc", true)]
    [InlineData(Names.PrimaryColor, "red", false)]
    [InlineData(Names.PrimaryColor, "#12345", false)]
    [InlineData(Names.PrimaryColor, "url(javascript:x)", false)]
    public async Task Catalog_values_are_checked_against_their_type(string name, string value, bool accepted)
    {
        using var scope = Build().CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        var act = () => manager.SetAsync(name, value, SettingScope.Global);

        if (accepted)
            await act.Should().NotThrowAsync();
        else
            await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_theme_outside_the_approved_list_is_refused()
    {
        using var scope = Build().CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        var act = () => manager.SetAsync(Names.ThemeMode, "neon", SettingScope.User);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Sender_address_cannot_be_set_at_company_scope()
    {
        // Mail sender identity is installation-wide, so a company cannot change where the platform's mail appears to come from.
        var provider = Build();
        using var scope = provider.CreateScope();
        var tenant = (CurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var inScope = tenant.Change(new TenantInfo(Guid.NewGuid(), "acme"));
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        var act = () => manager.SetAsync(Names.SenderAddress, "noreply@example.com", SettingScope.Tenant);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_company_can_override_the_application_name_and_others_see_the_default()
    {
        var provider = Build();
        var tenantId = Guid.NewGuid();
        using (var setup = provider.CreateScope())
        {
            var manager = setup.ServiceProvider.GetRequiredService<ISettingManager>();
            using var inCompany = ((CurrentTenant)setup.ServiceProvider.GetRequiredService<ICurrentTenant>()).Change(new TenantInfo(tenantId, "acme"));
            await manager.SetAsync(Names.AppName, "Acme ERP", SettingScope.Tenant);
            (await manager.GetOrNullAsync(Names.AppName)).Should().Be("Acme ERP");
        }

        using var other = provider.CreateScope();
        var otherManager = other.ServiceProvider.GetRequiredService<ISettingManager>();
        (await otherManager.GetOrNullAsync(Names.AppName)).Should().BeNull();
    }

    [Fact]
    public async Task A_sensitive_value_is_masked_in_the_administrator_list_but_readable_on_the_server()
    {
        var provider = Build(services => services.AddSingleton<ISettingDefinitionContributor>(new SecretContributor()));
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        await manager.SetAsync("Test.ApiKey", "sk-live-123", SettingScope.Global);

        var view = (await admin.ListAsync()).Single(v => v.Name == "Test.ApiKey");
        view.EffectiveValue.Should().Be(SettingMasking.Mask);
        view.IsSensitive.Should().BeTrue();
        (await manager.GetOrNullAsync("Test.ApiKey")).Should().Be("sk-live-123");
    }

    [Fact]
    public async Task A_sensitive_default_is_never_listed()
    {
        var provider = Build(services => services.AddSingleton<ISettingDefinitionContributor>(new SecretContributor()));
        using var scope = provider.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        var view = (await admin.ListAsync()).Single(v => v.Name == "Test.ApiKey");

        view.DefaultValue.Should().BeNull();
        view.EffectiveValue.Should().BeNull();
    }

    [Fact]
    public async Task A_contributed_definition_replaces_an_earlier_one_with_the_same_name()
    {
        var provider = Build(services => services.AddSingleton<ISettingDefinitionContributor>(new ReplacingContributor()));
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISettingManager>();

        (await manager.GetOrNullAsync(Names.DefaultLanguage)).Should().Be("de");
    }

    private static class Names
    {
        public const string SupportUrl = ApplicationSettingsCatalog.Names.SupportUrl;
        public const string SupportEmail = ApplicationSettingsCatalog.Names.SupportEmail;
        public const string PrimaryColor = ApplicationSettingsCatalog.Names.PrimaryColor;
        public const string ThemeMode = ApplicationSettingsCatalog.Names.ThemeMode;
        public const string SenderAddress = ApplicationSettingsCatalog.Names.SenderAddress;
        public const string AppName = ApplicationSettingsCatalog.Names.AppName;
        public const string DefaultLanguage = ApplicationSettingsCatalog.Names.DefaultLanguage;
    }

    private sealed class SecretContributor : ISettingDefinitionContributor
    {
        public void Contribute(ISettingDefinitionRegistry registry)
            => registry.Add(new SettingDefinition("Test.ApiKey", DefaultValue: "default-secret") { IsSensitive = true });
    }

    private sealed class ReplacingContributor : ISettingDefinitionContributor
    {
        public void Contribute(ISettingDefinitionRegistry registry)
            => registry.Add(new SettingDefinition(ApplicationSettingsCatalog.Names.DefaultLanguage, DefaultValue: "de"));
    }

    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => false;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
