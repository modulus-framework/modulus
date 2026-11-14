using FluentAssertions;
using Modulus.Cli.Commands;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The container build restores from copies of the local feeds, not from the generating machine's paths.</summary>
[Trait("Category", "Unit")]
[Collection(UxStateCollection.Name)]
public sealed class DockerNuGetFeedsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-docker-feeds-" + Guid.NewGuid().ToString("N"));

    public DockerNuGetFeedsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteNuGetConfig(params string[] localSources)
    {
        var adds = string.Concat(localSources.Select(s => $"    <add key=\"{s.Split('=')[0]}\" value=\"{s.Split('=')[1]}\" />\n"));
        File.WriteAllText(Path.Combine(_root, "NuGet.config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n    <clear />\n" +
            "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />\n" +
            adds + "  </packageSources>\n</configuration>\n");
    }

    [Fact]
    public void No_local_feed_writes_nothing()
    {
        WriteNuGetConfig();

        DockerNuGetFeeds.Prepare(_root).Should().BeEmpty();
        File.Exists(Path.Combine(_root, DockerNuGetFeeds.ConfigFile)).Should().BeFalse();
    }

    [Fact]
    public void A_local_feed_is_copied_into_the_build_context_and_the_docker_config_points_at_the_copy()
    {
        var feed = Path.Combine(_root, "nupkg");
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(feed, "Synvro.Modulus.Core.1.0.0.nupkg"), "pkg");
        WriteNuGetConfig("modulus-local=nupkg");

        var written = DockerNuGetFeeds.Prepare(_root);

        written.Should().Contain(DockerNuGetFeeds.ConfigFile);
        File.Exists(Path.Combine(_root, "deploy", "feed", "modulus-local", "Synvro.Modulus.Core.1.0.0.nupkg")).Should().BeTrue();
        var docker = File.ReadAllText(Path.Combine(_root, DockerNuGetFeeds.ConfigFile));
        docker.Should().Contain("value=\"/src/deploy/feed/modulus-local\"")
            .And.Contain("value=\"https://api.nuget.org/v3/index.json\"");
        // The solution's own NuGet.config keeps the path of the generating machine.
        File.ReadAllText(Path.Combine(_root, "NuGet.config")).Should().Contain("value=\"nupkg\"");
    }

    [Fact]
    public void A_missing_local_feed_fails_instead_of_producing_an_image_that_cannot_restore()
    {
        WriteNuGetConfig("modulus-local=does-not-exist");

        var act = () => DockerNuGetFeeds.Prepare(_root);

        act.Should().Throw<InvalidOperationException>().WithMessage("*does-not-exist*");
    }

    [Fact]
    public void The_Dockerfile_restores_with_the_docker_config_only_when_there_is_a_local_feed()
    {
        var engine = new TemplateEngine();
        var model = new AddDeploymentCommand.DeploymentModel { RootNamespace = "Shop", PublicHost = "shop.example.com", AcmeEmail = "ops@example.com" };

        engine.Render("deploy/Dockerfile", model).Should().NotContain("COPY deploy/nuget.docker.config");
        engine.Render("deploy/Dockerfile", new AddDeploymentCommand.DeploymentModel
        {
            RootNamespace = "Shop",
            PublicHost = "shop.example.com",
            AcmeEmail = "ops@example.com",
            LocalFeeds = true,
        }).Should().Contain("COPY deploy/nuget.docker.config ./NuGet.config");
    }
}
