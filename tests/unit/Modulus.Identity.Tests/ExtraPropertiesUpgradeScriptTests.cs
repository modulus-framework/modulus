using FluentAssertions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The identity upgrade scripts for each provider add the <c>ExtraProperties</c> column that <c>ModulusUser</c> maps, so an existing
/// deployment gets the column the model expects. Each script is idempotent (it checks before it adds).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExtraPropertiesUpgradeScriptTests
{
    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void Each_provider_script_adds_the_extra_properties_column_as_not_null(string provider)
    {
        var script = File.ReadAllText(ScriptPath(provider));

        var add = script.Split('\n').Single(l => !l.TrimStart().StartsWith("--") && l.Contains("add", StringComparison.OrdinalIgnoreCase) && l.Contains("ExtraProperties"));
        add.Should().Contain("NOT NULL", "the model maps the column as required");
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void Each_provider_script_checks_before_it_adds_the_extra_properties_column(string provider)
    {
        var script = File.ReadAllText(ScriptPath(provider));

        var guards = provider switch
        {
            "postgresql" => "ADD COLUMN IF NOT EXISTS \"ExtraProperties\"",
            "sqlserver" => "COL_LENGTH('$(user_table)', 'ExtraProperties') IS NULL",
            _ => "modulus_upgrade_add_column(@user_table, 'ExtraProperties'",
        };
        script.Should().Contain(guards, "a rerun must not fail on an existing column");
    }

    // Anchored on this source file, not the test output: the build output can live outside the repository.
    private static string ScriptPath(string provider, [System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "upgrades")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root (with docs/upgrades) must be above this test source file");

        return Path.Combine(directory!.FullName, "docs", "upgrades", $"identity-platform-2026-10.identity.{provider}.sql");
    }
}
