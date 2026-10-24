using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The branch scope (D-P4): a value for one organization unit of a company. The precedence is user, branch, company, installation,
/// default; a branch value never reaches another branch or another company, and it is refused without a branch in scope.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BranchSettingsTests
{
    private static readonly TenantInfo CompanyA = new(Guid.NewGuid(), "acme");
    private static readonly TenantInfo CompanyB = new(Guid.NewGuid(), "globex");
    private static readonly Guid BranchOne = Guid.NewGuid();
    private static readonly Guid BranchTwo = Guid.NewGuid();

    private readonly ServiceProvider _provider;
    private readonly CurrentTenant _tenant;
    private readonly FakeUser _user = new();
    private readonly FakeSecurity _security = new();

    public BranchSettingsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<ICurrentUser>(_user);
        services.AddSingleton<ISecurityContext>(_security);
        services.AddModulusSettings();
        _provider = services.BuildServiceProvider();
        _tenant = (CurrentTenant)_provider.GetRequiredService<ICurrentTenant>();
        _provider.GetRequiredService<ISettingDefinitionRegistry>().Add(new SettingDefinition("Shop.Tender", DefaultValue: "any"));
    }

    private ISettingManager Manager() => _provider.GetRequiredService<ISettingManager>();

    [Fact]
    public async Task A_branch_value_applies_to_its_branch_only()
    {
        using (_tenant.Change(CompanyA))
        {
            _security.BranchId = BranchOne;
            await Manager().SetAsync("Shop.Tender", "cash", SettingScope.Branch);

            (await Manager().ResolveAsync("Shop.Tender")).Should().BeEquivalentTo(new SettingResolution("Shop.Tender", "cash", SettingSource.Branch));

            _security.BranchId = BranchTwo;
            (await Manager().ResolveAsync("Shop.Tender")).Source.Should().Be(SettingSource.Default, "the other branch has no value");
        }
    }

    [Fact]
    public async Task Precedence_is_user_then_branch_then_company_then_installation()
    {
        using (_tenant.Change(CompanyA))
        {
            _security.BranchId = BranchOne;
            _user.UserId = Guid.NewGuid();

            await Manager().SetAsync("Shop.Tender", "G", SettingScope.Global);
            (await Manager().ResolveAsync("Shop.Tender")).Source.Should().Be(SettingSource.Global);

            await Manager().SetAsync("Shop.Tender", "T", SettingScope.Tenant);
            (await Manager().ResolveAsync("Shop.Tender")).Source.Should().Be(SettingSource.Tenant);

            await Manager().SetAsync("Shop.Tender", "B", SettingScope.Branch);
            (await Manager().ResolveAsync("Shop.Tender")).Should().BeEquivalentTo(new SettingResolution("Shop.Tender", "B", SettingSource.Branch));

            await Manager().SetAsync("Shop.Tender", "U", SettingScope.User);
            (await Manager().ResolveAsync("Shop.Tender")).Should().BeEquivalentTo(new SettingResolution("Shop.Tender", "U", SettingSource.User));
        }
    }

    [Fact]
    public async Task A_branch_value_of_one_company_is_not_seen_by_another_company_with_the_same_branch()
    {
        using (_tenant.Change(CompanyA))
        {
            _security.BranchId = BranchOne;
            await Manager().SetAsync("Shop.Tender", "cash", SettingScope.Branch);
        }

        using (_tenant.Change(CompanyB))
        {
            _security.BranchId = BranchOne;
            (await Manager().ResolveAsync("Shop.Tender")).Source.Should().Be(SettingSource.Default);
        }
    }

    [Fact]
    public async Task A_branch_value_is_refused_without_a_branch_in_scope()
    {
        using (_tenant.Change(CompanyA))
        {
            _security.BranchId = null;

            var act = () => Manager().SetAsync("Shop.Tender", "cash", SettingScope.Branch);

            await act.Should().ThrowAsync<ValidationException>().WithMessage("*branch*");
        }
    }

    [Fact]
    public async Task A_branch_value_is_refused_without_a_company_in_scope()
    {
        _security.BranchId = BranchOne;

        var act = () => Manager().SetAsync("Shop.Tender", "cash", SettingScope.Branch);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*company*");
    }

    [Fact]
    public async Task A_branch_value_is_refused_when_the_definition_does_not_allow_the_branch_scope()
    {
        _provider.GetRequiredService<ISettingDefinitionRegistry>()
            .Add(new SettingDefinition("Shop.Currency", DefaultValue: "USD") { AllowedScopes = SettingScopes.Global | SettingScopes.Tenant });

        using (_tenant.Change(CompanyA))
        {
            _security.BranchId = BranchOne;

            var act = () => Manager().SetAsync("Shop.Currency", "EUR", SettingScope.Branch);

            await act.Should().ThrowAsync<ValidationException>().WithMessage("*Branch scope*");
        }
    }

    [Fact]
    public async Task Removing_a_branch_value_falls_back_to_the_company()
    {
        using (_tenant.Change(CompanyA))
        {
            _security.BranchId = BranchOne;
            await Manager().SetAsync("Shop.Tender", "T", SettingScope.Tenant);
            await Manager().SetAsync("Shop.Tender", "B", SettingScope.Branch);

            await Manager().RemoveAsync("Shop.Tender", SettingScope.Branch);

            (await Manager().ResolveAsync("Shop.Tender")).Should().BeEquivalentTo(new SettingResolution("Shop.Tender", "T", SettingSource.Tenant));
        }
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? UserName => UserId.HasValue ? "test-user" : null;
        public string? Email => null;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class FakeSecurity : ISecurityContext
    {
        public SecurityPrincipalKind Kind => SecurityPrincipalKind.User;
        public ICurrentUser User => throw new NotSupportedException();
        public Guid? CompanyId => null;
        public Guid? GroupId => null;
        public Guid? BranchId { get; set; }
        public NetworkContext Network => NetworkContext.Unverified;
        public AgentContext? Agent => null;
        public string? CorrelationId => null;
    }
}
