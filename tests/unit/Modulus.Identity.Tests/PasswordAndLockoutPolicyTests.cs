using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// <c>Identity:Password</c> and <c>Identity:Lockout</c> drive the ASP.NET Core Identity options at registration. The
/// defaults must reproduce the framework's historical rules, so an app that sets nothing sees no change.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasswordAndLockoutPolicyTests
{
    private static IdentityOptions ResolveIdentityOptions(IDictionary<string, string?>? settings = null, Action<IdentityOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration, configure);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
    }

    [Fact]
    public void Defaults_keep_the_historical_password_rules()
    {
        var options = ResolveIdentityOptions();

        options.Password.RequiredLength.Should().Be(8);
        options.Password.RequireDigit.Should().BeTrue();
        options.Password.RequireUppercase.Should().BeTrue();
        options.Password.RequireLowercase.Should().BeFalse();
        options.Password.RequireNonAlphanumeric.Should().BeFalse();
        options.Password.RequiredUniqueChars.Should().Be(1);
    }

    [Fact]
    public void Defaults_keep_the_asp_net_lockout_rules()
    {
        var options = ResolveIdentityOptions();

        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Password_policy_is_bound_from_configuration()
    {
        var options = ResolveIdentityOptions(new Dictionary<string, string?>
        {
            ["Identity:Password:RequiredLength"] = "12",
            ["Identity:Password:RequiredUniqueChars"] = "4",
            ["Identity:Password:RequireDigit"] = "false",
            ["Identity:Password:RequireLowercase"] = "true",
            ["Identity:Password:RequireNonAlphanumeric"] = "true",
        });

        options.Password.RequiredLength.Should().Be(12);
        options.Password.RequiredUniqueChars.Should().Be(4);
        options.Password.RequireDigit.Should().BeFalse();
        options.Password.RequireLowercase.Should().BeTrue();
        options.Password.RequireNonAlphanumeric.Should().BeTrue();
    }

    [Fact]
    public void Lockout_is_bound_from_configuration()
    {
        var options = ResolveIdentityOptions(new Dictionary<string, string?>
        {
            ["Identity:Lockout:MaxFailedAccessAttempts"] = "3",
            ["Identity:Lockout:DefaultLockoutMinutes"] = "15",
        });

        options.Lockout.MaxFailedAccessAttempts.Should().Be(3);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Lockout_can_be_disabled_from_configuration()
    {
        var options = ResolveIdentityOptions(new Dictionary<string, string?>
        {
            ["Identity:Lockout:Enabled"] = "false",
        });

        options.Lockout.AllowedForNewUsers.Should().BeFalse();
    }

    [Fact]
    public void Explicit_configureIdentity_callback_still_wins_over_configuration()
    {
        var options = ResolveIdentityOptions(
            new Dictionary<string, string?> { ["Identity:Password:RequiredLength"] = "12" },
            o => o.Password.RequiredLength = 16);

        options.Password.RequiredLength.Should().Be(16);
    }

    [Theory]
    [InlineData("Identity:Password:RequiredLength", "0", "RequiredLength")]
    [InlineData("Identity:Password:RequiredUniqueChars", "0", "RequiredUniqueChars")]
    [InlineData("Identity:Lockout:MaxFailedAccessAttempts", "0", "MaxFailedAccessAttempts")]
    [InlineData("Identity:Lockout:DefaultLockoutMinutes", "0", "DefaultLockoutMinutes")]
    [InlineData("Identity:Password:HistoryCount", "-1", "HistoryCount")]
    [InlineData("Identity:Password:HistoryCount", "51", "HistoryCount")]
    [InlineData("Identity:Password:MaxAgeDays", "-1", "MaxAgeDays")]
    public void Nonsensical_values_fail_at_registration(string key, string value, string property)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
        var services = new ServiceCollection();

        var act = () => services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{property}*");
    }
}
