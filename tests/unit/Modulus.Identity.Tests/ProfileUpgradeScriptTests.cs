using FluentAssertions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The profile upgrade scripts (premodule 3) for each provider add the four new columns and the company-scoped employee-code index, so an
/// existing deployment gets the columns the model maps. The scripts were also run against a real PostgreSQL, MySQL and SQL Server
/// schema (idempotent rerun, data preserved, duplicate code refused per company, NULL codes allowed).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProfileUpgradeScriptTests
{
    private static readonly string[] Providers = ["postgresql", "sqlserver", "mysql"];
    private static readonly string[] Columns = ["WorkEmail", "JobTitle", "Department", "PrimaryBranchId"];

    [Theory]
    [MemberData(nameof(ProviderScripts))]
    public void Each_provider_script_adds_the_profile_columns_and_the_employee_code_index(string provider)
    {
        var script = File.ReadAllText(ScriptPath(provider));

        foreach (var column in Columns)
            script.Should().Contain(column, $"the {provider} profile script must add {column}");
        script.Should().Contain("IX_AspNetUsers_TenantId_EmployeeCode_Unique", "employee codes are unique within a company");
        // The column definitions (not the index filter) must be nullable.
        var columnLines = script.Split('\n').Where(line => Columns.Any(line.Contains)).ToList();
        columnLines.Should().NotBeEmpty();
        columnLines.Should().OnlyContain(line => !line.Contains("NOT NULL"), "new profile columns are nullable: no profile field becomes required");
    }

    [Theory]
    [MemberData(nameof(ProviderScripts))]
    public void Each_provider_script_refuses_duplicate_codes_before_it_changes_anything_and_creates_the_unique_index_before_dropping_the_old_one(string provider)
    {
        var script = File.ReadAllText(ScriptPath(provider));

        script.Should().Contain("REFUSED: duplicate employee codes", "the preflight must stop the upgrade with a diagnostic");
        var preflight = script.IndexOf("REFUSED", StringComparison.Ordinal);
        var firstChange = script.IndexOf("ADD", StringComparison.Ordinal);
        preflight.Should().BeLessThan(firstChange, "no column is added before the duplicate check");
        var create = script.IndexOf("IX_AspNetUsers_TenantId_EmployeeCode_Unique", StringComparison.Ordinal);
        var drop = script.LastIndexOf("IX_AspNetUsers_TenantId_EmployeeCode", StringComparison.Ordinal);
        create.Should().BeLessThan(script.IndexOf("DROP", StringComparison.Ordinal) > 0 ? script.LastIndexOf("DROP", StringComparison.Ordinal) : int.MaxValue,
            "the unique index exists before the old one goes");
        drop.Should().BeGreaterThan(create);
    }

    public static IEnumerable<object[]> ProviderScripts() => Providers.Select(p => new object[] { p });

    private static string ScriptPath(string provider)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "upgrades")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root (with docs/upgrades) must be above the test output");

        return Path.Combine(directory!.FullName, "docs", "upgrades", $"identity-platform-2026-10.profile.{provider}.sql");
    }
}
