using FluentAssertions;
using Modulus.Cli.Commands;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary><c>doctor</c> must not fail an infrastructure-only module (Identity, Audit, Webhooks) for naming its context <c>App{Module}DbContext</c>.</summary>
[Trait("Category", "Unit")]
public sealed class DoctorModuleChecksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-doctor-" + Guid.NewGuid().ToString("N"));

    public DoctorModuleChecksTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("IdentityDbContext.cs")]
    [InlineData("AppIdentityDbContext.cs")]
    public void A_context_file_is_found_whatever_its_prefix(string file)
    {
        File.WriteAllText(Path.Combine(_root, file), "// context");

        DoctorCommand.CheckAnyFile("Identity: DbContext", _root, "*DbContext.cs", "IdentityDbContext.cs", _root)
            .Kind.Should().Be(DoctorCommand.CheckKind.Pass);
    }

    [Fact]
    public void A_missing_context_still_fails()
    {
        File.WriteAllText(Path.Combine(_root, "Other.cs"), "// not a context");

        DoctorCommand.CheckAnyFile("Identity: DbContext", _root, "*DbContext.cs", "IdentityDbContext.cs", _root)
            .Kind.Should().Be(DoctorCommand.CheckKind.Fail);
    }

    [Fact]
    public void A_factory_is_not_mistaken_for_the_context()
    {
        File.WriteAllText(Path.Combine(_root, "AppIdentityDbContextFactory.cs"), "// factory");

        DoctorCommand.CheckAnyFile("Identity: DbContext", _root, "*DbContext.cs", "IdentityDbContext.cs", _root)
            .Kind.Should().Be(DoctorCommand.CheckKind.Fail);
    }
}
