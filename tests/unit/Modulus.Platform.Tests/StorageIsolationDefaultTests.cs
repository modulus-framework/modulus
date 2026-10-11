namespace Modulus.Platform.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.MultiTenancy.Extensions;
using Modulus.Storage;
using Xunit;

/// <summary>Tenant isolation of file storage follows multi-tenancy unless the app opts out.</summary>
[Trait("Category", "Unit")]
public sealed class StorageIsolationDefaultTests
{
    [Fact]
    public void Isolation_is_on_by_default_once_multi_tenancy_is_registered()
    {
        var services = new ServiceCollection();
        services.AddMultiTenancy();

        services.BuildServiceProvider().GetRequiredService<IOptions<StorageOptions>>().Value.IsolateTenants.Should().BeTrue();
    }

    [Fact]
    public void Isolation_stays_off_without_multi_tenancy()
    {
        var services = new ServiceCollection();
        services.AddOptions<StorageOptions>();

        services.BuildServiceProvider().GetRequiredService<IOptions<StorageOptions>>().Value.IsolateTenants.Should().BeNull();
    }

    [Fact]
    public void An_explicit_opt_out_is_kept()
    {
        var services = new ServiceCollection();
        services.Configure<StorageOptions>(o => o.IsolateTenants = false);
        services.AddMultiTenancy();

        services.BuildServiceProvider().GetRequiredService<IOptions<StorageOptions>>().Value.IsolateTenants.Should().BeFalse();
    }
}
