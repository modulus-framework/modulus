using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Setting changes are written to the security audit: who, where, what changed, and never a secret's value.</summary>
[Trait("Category", "Unit")]
public sealed class SettingAuditTests
{
    private sealed class CapturingAuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    private sealed class SecretContributor : ISettingDefinitionContributor
    {
        public void Contribute(ISettingDefinitionRegistry registry)
            => registry.Add(new SettingDefinition("Test.ApiKey") { IsSensitive = true });
    }

    private static (IServiceProvider Services, CurrentTenant Tenant, CurrentUserStub User) Build(CapturingAuditLog? audit)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        var user = new CurrentUserStub();
        services.AddSingleton<ICurrentUser>(user);
        services.AddModulusApplicationSettings();
        services.AddSingleton<ISettingDefinitionContributor>(new SecretContributor());
        if (audit is not null)
            services.AddSingleton<ISecurityAuditLog>(audit);
        var provider = services.BuildServiceProvider();
        return (provider, (CurrentTenant)provider.GetRequiredService<ICurrentTenant>(), user);
    }

    [Fact]
    public async Task A_changed_value_is_recorded_with_its_scope_actor_company_and_before_and_after()
    {
        var audit = new CapturingAuditLog();
        var (services, tenant, user) = Build(audit);
        var company = Guid.NewGuid();
        var admin = Guid.NewGuid();
        user.UserId = admin;
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        using (tenant.Change(new TenantInfo(company, "acme")))
            await settings.SetAsync(ApplicationSettingsCatalog.Names.AppName, "Acme ERP", SettingScope.Tenant);

        var e = audit.Events.Should().ContainSingle().Subject;
        e.Category.Should().Be(SettingAdministration.AuditCategory);
        e.Action.Should().Be(SettingAdministration.ChangedAction);
        e.Target.Should().Be(ApplicationSettingsCatalog.Names.AppName);
        e.TenantId.Should().Be(company);
        e.Actor.Should().Be(admin.ToString("D"));
        e.Details["scope"].Should().Be("Tenant");
        e.Details["sensitive"].Should().Be("false");
        e.Details["before"].Should().BeNull("nothing applied before the change");
        e.Details["after"].Should().Be("Acme ERP");
    }

    [Fact]
    public async Task A_removed_value_is_recorded_as_a_removal()
    {
        var audit = new CapturingAuditLog();
        var (services, _, _) = Build(audit);
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();
        await settings.SetAsync(ApplicationSettingsCatalog.Names.AppName, "Host", SettingScope.Global);

        await settings.RemoveAsync(ApplicationSettingsCatalog.Names.AppName, SettingScope.Global);

        audit.Events.Should().HaveCount(2);
        audit.Events[1].Action.Should().Be(SettingAdministration.RemovedAction);
        audit.Events[1].Details["before"].Should().Be("Host");
        audit.Events[1].Details["after"].Should().BeNull();
    }

    [Fact]
    public async Task A_secret_is_recorded_as_changed_with_no_value_anywhere_in_the_event()
    {
        var audit = new CapturingAuditLog();
        var (services, _, _) = Build(audit);
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        await settings.SetAsync("Test.ApiKey", "sk-live-very-secret", SettingScope.Global);
        await settings.SetAsync("Test.ApiKey", "sk-live-second-secret", SettingScope.Global);

        var e = audit.Events[1];
        e.Details["sensitive"].Should().Be("true");
        e.Details.Should().NotContainKey("before");
        e.Details.Should().NotContainKey("after");
        var everything = string.Join("|", e.Details.Values.Append(e.Target));
        everything.Should().NotContain("sk-live");
    }

    [Fact]
    public async Task A_refused_change_records_nothing()
    {
        var audit = new CapturingAuditLog();
        var (services, _, _) = Build(audit);
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        var act = () => settings.SetAsync(ApplicationSettingsCatalog.Names.SupportUrl, "javascript:alert(1)", SettingScope.Global);

        await act.Should().ThrowAsync<ValidationException>();
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_an_audit_log_changes_still_work()
    {
        var (services, _, _) = Build(audit: null);
        using var scope = services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingAdministration>();

        await settings.SetAsync(ApplicationSettingsCatalog.Names.AppName, "Works", SettingScope.Global);

        (await scope.ServiceProvider.GetRequiredService<ISettingManager>().GetOrNullAsync(ApplicationSettingsCatalog.Names.AppName))
            .Should().Be("Works");
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
