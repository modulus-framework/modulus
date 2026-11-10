using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// In a multi-company host a first-boot grant is written in the host context, which no company sees, so <c>generate-crud</c> must put
/// each new entity's permission into <c>CompanyProvisioning:Grants:Admin</c>; otherwise every module after the example one is 403 for
/// the administrators of every company.
/// </summary>
[Collection(UxStateCollection.Name)]
[Trait("Category", "Unit")]
public sealed class CrudCompanyGrantTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modulus-crud-grant-" + Guid.NewGuid().ToString("N"));

    public CrudCompanyGrantTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string Settings => Path.Combine(_dir, "appsettings.json");

    [Fact]
    public void The_permission_joins_the_admin_grants_once()
    {
        File.WriteAllText(Settings, "{\n  \"CompanyProvisioning\": {\n    \"Grants\": {\n      \"Admin\": [ \"catalog:products:manage\" ]\n    }\n  }\n}\n");

        GenerateCrudCommand.EnsureCompanyAdminGrant(Settings, "orders:orders:manage").Should().BeTrue();
        GenerateCrudCommand.EnsureCompanyAdminGrant(Settings, "orders:orders:manage").Should().BeFalse("a second run changes nothing");

        var admin = WebhooksWiringTests.Parse(File.ReadAllText(Settings))
            .GetProperty("CompanyProvisioning").GetProperty("Grants").GetProperty("Admin")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        admin.Should().Equal("catalog:products:manage", "orders:orders:manage");
    }

    [Fact]
    public void A_host_without_company_grants_is_left_alone()
    {
        File.WriteAllText(Settings, "{\n  \"Logging\": {}\n}\n");

        GenerateCrudCommand.EnsureCompanyAdminGrant(Settings, "orders:orders:manage").Should().BeFalse();
        File.ReadAllText(Settings).Should().NotContain("orders:orders:manage");
    }

    [Fact]
    public void A_missing_settings_file_is_not_an_error()
        => GenerateCrudCommand.EnsureCompanyAdminGrant(Settings, "orders:orders:manage").Should().BeFalse();
}
