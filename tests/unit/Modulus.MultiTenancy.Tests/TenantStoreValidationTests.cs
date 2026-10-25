using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// A multi-company host must build with build-time validation on, as it does in Development, even when the application registers no
/// OpenBao secret writer. The data-store provisioner needs one, so a type registration would fail the whole host at startup.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantStoreValidationTests
{
    [Fact]
    public void The_tenant_store_host_builds_with_validation_on_and_no_secret_writer()
    {
        var services = new ServiceCollection();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite("Data Source=:memory:"));

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        act.Should().NotThrow();
    }

    [Fact]
    public void Provisioning_without_a_secret_writer_fails_with_a_clear_message_when_it_is_used()
    {
        var services = new ServiceCollection();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite("Data Source=:memory:"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ITenantDataStoreProvisioner>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ISecretWriter*");
    }
}
