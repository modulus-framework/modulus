using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>Shop-floor accounts and PIN verification, through the real <c>AddModulusIdentity</c> + <c>AddModulusShopFloor</c> registration.</summary>
[Trait("Category", "Unit")]
public sealed class ShopFloorTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid OtherCompany = Guid.NewGuid();

    [Fact]
    public async Task A_created_operator_signs_in_with_code_and_pin()
    {
        await using var h = await Harness.CreateAsync();
        (await h.Accounts.CreateAsync(Company, "e-1042", "482913", roles: ["ShopFloor"])).Succeeded.Should().BeTrue();

        var grant = await h.Validator.ValidateAsync(Company, "E-1042", "482913");

        grant.Success.Should().BeTrue();
        grant.TenantId.Should().Be(Company);
        grant.Roles.Should().Equal("ShopFloor");
    }

    [Fact]
    public async Task The_token_carries_only_the_floor_roles()
    {
        await using var h = await Harness.CreateAsync();
        await h.Roles.CreateAsync(new ModulusRole { Name = "Accountant", TenantId = Company });
        await h.Accounts.CreateAsync(Company, "E1", "482913", roles: ["ShopFloor", "Accountant"]);

        (await h.Validator.ValidateAsync(Company, "E1", "482913")).Roles.Should().Equal("ShopFloor");
    }

    [Fact]
    public async Task A_wrong_pin_is_refused_and_locks_the_account_after_repeated_failures()
    {
        await using var h = await Harness.CreateAsync();
        await h.Accounts.CreateAsync(Company, "E1", "482913");

        for (var i = 0; i < 5; i++)
            (await h.Validator.ValidateAsync(Company, "E1", "000012")).Success.Should().BeFalse();

        // Locked: even the right PIN is refused.
        (await h.Validator.ValidateAsync(Company, "E1", "482913")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Setting_a_new_pin_clears_the_lock_out()
    {
        await using var h = await Harness.CreateAsync();
        await h.Accounts.CreateAsync(Company, "E1", "482913");
        for (var i = 0; i < 5; i++)
            await h.Validator.ValidateAsync(Company, "E1", "000012");
        var user = await h.Users.Users.SingleAsync(u => u.EmployeeCode == "E1");

        (await h.Accounts.SetPinAsync(user.Id, "7391")).Succeeded.Should().BeTrue();

        (await h.Validator.ValidateAsync(Company, "E1", "7391")).Success.Should().BeTrue();
        (await h.Validator.ValidateAsync(Company, "E1", "482913")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task An_operator_of_another_company_cannot_sign_in_here()
    {
        await using var h = await Harness.CreateAsync();
        await h.Accounts.CreateAsync(Company, "E1", "482913");

        (await h.Validator.ValidateAsync(OtherCompany, "E1", "482913")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task The_same_code_can_exist_in_two_companies_but_not_twice_in_one()
    {
        await using var h = await Harness.CreateAsync();
        (await h.Accounts.CreateAsync(Company, "E1", "482913")).Succeeded.Should().BeTrue();
        (await h.Accounts.CreateAsync(OtherCompany, "E1", "391847")).Succeeded.Should().BeTrue();

        var duplicate = await h.Accounts.CreateAsync(Company, "e1", "482913");

        duplicate.Errors.Should().Contain(e => e.Code == "DuplicateEmployeeCode");
    }

    [Fact]
    public async Task A_disabled_operator_is_refused()
    {
        await using var h = await Harness.CreateAsync();
        await h.Accounts.CreateAsync(Company, "E1", "482913");
        var user = await h.Users.Users.SingleAsync(u => u.EmployeeCode == "E1");
        user.IsActive = false;
        await h.Users.UpdateAsync(user);

        (await h.Validator.ValidateAsync(Company, "E1", "482913")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_shop_floor_account_has_no_password()
    {
        await using var h = await Harness.CreateAsync();
        await h.Accounts.CreateAsync(Company, "E1", "482913");
        var user = await h.Users.Users.SingleAsync(u => u.EmployeeCode == "E1");

        user.PasswordHash.Should().BeNull();
        (await h.Users.CheckPasswordAsync(user, "482913")).Should().BeFalse();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12a456")]
    [InlineData("1111")]
    [InlineData("")]
    public async Task A_weak_or_malformed_pin_is_refused(string pin)
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Accounts.CreateAsync(Company, "E1", pin);

        result.Errors.Should().Contain(e => e.Code == "InvalidPin");
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public async Task A_malformed_employee_code_is_refused(string code)
    {
        await using var h = await Harness.CreateAsync();

        (await h.Accounts.CreateAsync(Company, code, "482913")).Errors.Should().Contain(e => e.Code == "InvalidEmployeeCode");
    }

    [Fact]
    public async Task A_missing_role_is_refused_and_no_account_is_left_behind()
    {
        await using var h = await Harness.CreateAsync();

        var result = await h.Accounts.CreateAsync(Company, "E1", "482913", roles: ["Nope"]);

        result.Errors.Should().Contain(e => e.Code == "RoleNotFound");
        (await h.Users.Users.CountAsync(u => u.EmployeeCode == "E1")).Should().Be(0);
    }

    [Fact]
    public void Invalid_settings_are_reported()
    {
        new ShopFloorOptions { Enabled = true, MinPinLength = 3 }.IsValidForTest().Should().BeFalse();
        new ShopFloorOptions { Enabled = true, AccessTokenMinutes = 0 }.IsValidForTest().Should().BeFalse();
        new ShopFloorOptions { Enabled = true }.IsValidForTest().Should().BeTrue();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private Harness(SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope)
        {
            _connection = connection;
            _provider = provider;
            _scope = scope;
        }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:ShopFloor:Enabled"] = "true",
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
            services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);
            services.AddModulusShopFloor<ModulusUser, ModulusRole>(configuration);

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
            var harness = new Harness(connection, provider, scope);
            await harness.Roles.CreateAsync(new ModulusRole { Name = "ShopFloor", TenantId = Company });
            return harness;
        }

        public UserManager<ModulusUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

        public RoleManager<ModulusRole> Roles => _scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();

        public IShopFloorAccountService Accounts => _scope.ServiceProvider.GetRequiredService<IShopFloorAccountService>();

        internal IShopFloorCredentialValidator Validator => _scope.ServiceProvider.GetRequiredService<IShopFloorCredentialValidator>();

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

internal static class ShopFloorOptionsTestExtensions
{
    public static bool IsValidForTest(this ShopFloorOptions options) => options.IsValid(out _);
}
