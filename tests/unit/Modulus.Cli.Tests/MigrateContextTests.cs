using FluentAssertions;
using Modulus.Cli.Commands;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>`modulus migrate add` names the module's own DbContext, so dotnet ef never has to choose between several.</summary>
[Trait("Category", "Unit")]
public sealed class MigrateContextTests
{
    [Fact]
    public void The_module_context_is_found_and_its_design_time_factory_is_skipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modulus-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "AppIdentityDbContextFactory.cs"), "public sealed class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext> { }");
            File.WriteAllText(Path.Combine(dir, "AppIdentityDbContext.cs"), "public sealed class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : ModulusIdentityDbContext(options) { }");

            MigrateSupport.FindDbContextName(dir).Should().Be("AppIdentityDbContext");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void No_context_file_means_no_name_and_dotnet_ef_chooses()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modulus-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Repository.cs"), "public sealed class Repository { }");

            MigrateSupport.FindDbContextName(dir).Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
