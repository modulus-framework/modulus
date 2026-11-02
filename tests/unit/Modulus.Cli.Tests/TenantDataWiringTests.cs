using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The host and module edits that register a module's tenant-data handler (<c>add-tenant-lifecycle</c>).</summary>
[Trait("Category", "Unit")]
public sealed class TenantDataWiringTests
{
    private const string Host = """
        using Shop.Modules.Catalog.Infrastructure;
        using Modulus.MultiTenancy;

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddTenantLifecycle(builder.Configuration);
        """;

    [Fact]
    public void The_plain_call_is_widened_to_register_the_handler_once()
    {
        var once = TenantLifecycleWiring.EnsureHandlerRegistration(Host, "CatalogTenantDataHandler", "Shop.Modules.Catalog.Infrastructure.TenantData");

        once.Should().Contain("builder.Services.AddTenantLifecycle(builder.Configuration, lifecycle => lifecycle")
            .And.Contain(".AddHandler<CatalogTenantDataHandler>());")
            .And.Contain("using Shop.Modules.Catalog.Infrastructure.TenantData;");

        var twice = TenantLifecycleWiring.EnsureHandlerRegistration(once, "CatalogTenantDataHandler", "Shop.Modules.Catalog.Infrastructure.TenantData");
        twice.Should().Be(once);
    }

    [Fact]
    public void A_host_without_the_plain_call_is_left_alone()
    {
        const string custom = "builder.Services.AddTenantLifecycle(builder.Configuration, o => o.RetentionDriver = 0);";

        TenantLifecycleWiring.EnsureHandlerRegistration(custom, "CatalogTenantDataHandler", "Shop.X").Should().Be(custom);
    }

    [Fact]
    public void The_module_project_references_the_lifecycle_package_once()
    {
        const string csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n  </ItemGroup>\n</Project>\n";

        var once = TenantLifecycleWiring.EnsurePackageReference(csproj, "2.0.0");

        once.Should().Contain("<PackageReference Include=\"Synvro.Modulus.Tenancy.Lifecycle\" Version=\"2.0.0\" />");
        TenantLifecycleWiring.EnsurePackageReference(once, "2.0.0").Should().Be(once);
    }
}
