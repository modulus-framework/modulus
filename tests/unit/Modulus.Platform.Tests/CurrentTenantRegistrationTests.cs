using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// <c>AddModulus</c> (and others) <c>TryAdd</c> <see cref="NullCurrentTenant"/>, which is always the host. A generated
/// Program.cs calls <c>AddMultiTenancy</c> after <c>AddModulus</c>, and a plain <c>TryAdd</c> there kept the null tenant:
/// every query saw every company.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CurrentTenantRegistrationTests
{
    [Fact]
    public void AddMultiTenancy_replaces_the_null_default_registered_before_it()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.TryAddScoped<ICurrentTenant, NullCurrentTenant>();

        services.AddMultiTenancy();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var tenant = provider.GetRequiredService<ICurrentTenant>();
        tenant.Should().BeOfType<CurrentTenant>();
        tenant.IsHost.Should().BeFalse("no company in scope is not the host");
        services.Count(d => d.ServiceType == typeof(ICurrentTenant)).Should().Be(1);
    }

    [Fact]
    public void A_custom_tenant_accessor_registered_before_AddMultiTenancy_is_kept()
    {
        var custom = new NullCurrentTenant();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_ => custom);

        services.AddMultiTenancy();
        services.AddMultiTenancy();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(custom);
    }

    [Fact]
    public void A_null_default_added_after_AddMultiTenancy_changes_nothing()
    {
        var services = new ServiceCollection();
        services.AddMultiTenancy();
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
    }
}
