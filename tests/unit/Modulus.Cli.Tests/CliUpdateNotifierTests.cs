using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class CliUpdateNotifierTests
{
    [Fact]
    public void BuildNotice_names_both_versions_and_the_update_command_when_a_newer_version_exists()
    {
        var notice = CliUpdateNotifier.BuildNotice(latest: "1.5.2", installed: "1.5.1");

        notice.Should().NotBeNull();
        notice.Should().Contain("1.5.2").And.Contain("you have 1.5.1");
        notice.Should().Contain("dotnet tool update -g Synvro.Modulus.Cli");
    }

    [Theory]
    [InlineData("1.5.1", "1.5.1")]
    [InlineData("1.5.0", "1.5.1")]
    public void BuildNotice_is_silent_when_the_installed_version_is_current_or_newer(string latest, string installed)
    {
        CliUpdateNotifier.BuildNotice(latest, installed).Should().BeNull();
    }

    [Fact]
    public void BuildNotice_is_silent_when_either_version_is_unknown()
    {
        CliUpdateNotifier.BuildNotice(latest: null, installed: "1.5.1").Should().BeNull();
        CliUpdateNotifier.BuildNotice(latest: "1.5.2", installed: null).Should().BeNull();
    }

    [Fact]
    public async Task LatestVersionAsync_answers_from_a_fresh_cache_without_asking_nuget()
    {
        var root = Path.Combine(Path.GetTempPath(), "modulus-update-" + Guid.NewGuid().ToString("N"));
        var previous = UiTemplatePackage.UserRoot;
        try
        {
            UiTemplatePackage.UserRoot = root;
            Directory.CreateDirectory(root);
            var checkedAt = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
            await File.WriteAllTextAsync(Path.Combine(root, "update-check.json"),
                $$"""{"CheckedAt":"{{checkedAt}}","Latest":"9.9.9"}""");

            var latest = await CliUpdateNotifier.LatestVersionAsync();

            latest.Should().Be("9.9.9");
        }
        finally
        {
            UiTemplatePackage.UserRoot = previous;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
