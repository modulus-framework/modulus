using FluentAssertions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The tenancy upgrade scripts add the company columns that the entity maps. Nothing else checks the scripts against the EF model, so a
/// renamed property or a script that forgets a column would only show at runtime on an upgraded database. These tests fail first.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenancyUpgradeScriptTests
{
    private static readonly string[] Providers = ["sqlserver", "postgresql", "mysql"];

    // The columns added to ModulusTenants since the base schema, named by their entity property.
    private static readonly string[] AddedColumns =
    [
        nameof(TenantEntity.Status),
        nameof(TenantEntity.TrialEndsAt),
        nameof(TenantEntity.MaxUsers),
        nameof(TenantEntity.ParentId),
    ];

    [Theory]
    [MemberData(nameof(ProviderScripts))]
    public void The_tenancy_script_adds_every_company_column_the_entity_maps(string provider)
    {
        var script = File.ReadAllText(ScriptPath(provider));

        foreach (var column in AddedColumns)
            script.Should().Contain(column, $"the {provider} tenancy upgrade script must add the {column} column of ModulusTenants");
    }

    [Fact]
    public void The_entity_still_maps_every_column_the_scripts_add()
    {
        // Reflection on the names: a renamed property fails here, next to the script it would break.
        var properties = typeof(TenantEntity).GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var column in AddedColumns)
            properties.Should().Contain(column, "the scripts add this column, so the entity must map it under the same name");
    }

    public static IEnumerable<object[]> ProviderScripts() => Providers.Select(p => new object[] { p });

    private static string ScriptPath(string provider)
    {
        // The test runs from its own bin folder; the scripts live in docs/upgrades at the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "upgrades")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root (with docs/upgrades) must be above the test output");

        return Path.Combine(directory!.FullName, "docs", "upgrades", $"identity-platform-2026-10.tenancy.{provider}.sql");
    }
}
