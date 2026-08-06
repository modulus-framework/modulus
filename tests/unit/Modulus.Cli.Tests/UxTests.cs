using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class UxTests
{
    [Fact]
    public void Reset_clears_all_flags()
    {
        // Set all flags
        Ux.DryRun = true;
        Ux.Force = true;
        Ux.Verbose = true;
        Ux.Quiet = true;

        Ux.Reset();

        Ux.DryRun.Should().BeFalse();
        Ux.Force.Should().BeFalse();
        Ux.Verbose.Should().BeFalse();
        Ux.Quiet.Should().BeFalse();
    }

    [Fact]
    public void IsInteractive_returns_false_under_test_runner()
    {
        // Test runners redirect stdin, so IsInteractive should be false.
        Ux.IsInteractive.Should().BeFalse();
    }

    [Fact]
    public void WriteFile_writes_content_when_not_dry_run()
    {
        Ux.Reset();
        var path = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid()}.txt");
        try
        {
            Ux.WriteFile(path, "hello");
            File.Exists(path).Should().BeTrue();
            File.ReadAllText(path).Should().Be("hello");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteFile_skips_write_under_dry_run()
    {
        Ux.Reset();
        Ux.DryRun = true;
        var path = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid()}.txt");
        try
        {
            Ux.WriteFile(path, "hello");
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Ux.Reset();
        }
    }

    [Fact]
    public void WriteFile_creates_parent_directories()
    {
        Ux.Reset();
        var dir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "sub", "file.txt");
        try
        {
            Ux.WriteFile(path, "nested");
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CreateDirectory_is_noop_under_dry_run()
    {
        Ux.Reset();
        Ux.DryRun = true;
        var dir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid()}");
        try
        {
            Ux.CreateDirectory(dir);
            Directory.Exists(dir).Should().BeFalse();
        }
        finally
        {
            Ux.Reset();
        }
    }

    [Fact]
    public void DeleteDirectory_is_noop_under_dry_run()
    {
        Ux.Reset();
        Ux.DryRun = true;
        var dir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid()}");
        try
        {
            Ux.DeleteDirectory(dir);
            // Should not throw even though directory doesn't exist
        }
        finally
        {
            Ux.Reset();
        }
    }

    [Fact]
    public void DeleteDirectory_deletes_existing_directory()
    {
        Ux.Reset();
        var dir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "file.txt"), "content");
        try
        {
            Ux.DeleteDirectory(dir);
            Directory.Exists(dir).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Confirm_returns_non_interactive_default_when_not_interactive()
    {
        Ux.Reset();
        // Under a test runner stdin is redirected, so IsInteractive is false.
        Ux.Confirm("prompt?", nonInteractiveDefault: false).Should().BeFalse();
        Ux.Confirm("prompt?", nonInteractiveDefault: true).Should().BeTrue();
    }

    [Fact]
    public void Confirm_returns_true_when_force_is_set()
    {
        Ux.Reset();
        Ux.Force = true;
        Ux.Confirm("prompt?", nonInteractiveDefault: false).Should().BeTrue();
        Ux.Reset();
    }

    [Fact]
    public void SelectOrFallback_returns_fallback_when_not_interactive()
    {
        Ux.Reset();
        var result = Ux.SelectOrFallback("pick one?", new[] { "A", "B" }, "B");
        result.Should().Be("B");
    }

    [Fact]
    public void AskOrFallback_returns_fallback_when_not_interactive()
    {
        Ux.Reset();
        var result = Ux.AskOrFallback("name?", "default");
        result.Should().Be("default");
    }

    [Fact]
    public void AskRequired_throws_when_not_interactive()
    {
        Ux.Reset();
        var act = () => Ux.AskRequired("name?");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Missing required argument*");
    }

    [Fact]
    public void AskRequired_includes_ciHint_in_error()
    {
        Ux.Reset();
        var act = () => Ux.AskRequired("name?", ciHint: "Run: modulus app MyApp");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Run: modulus app MyApp*");
    }
}
