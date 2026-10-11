using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class UiFileGeneratorTests
{
    private readonly ITemplatePackageResolver _resolver = new TemplatePackageResolver();
    private readonly UiFileGenerator _generator;

    public UiFileGeneratorTests()
    {
        _generator = new UiFileGenerator(_resolver);
    }

    private ModuleModel CreateTestModule()
    {
        return new ModuleModel
        {
            RootNamespace = "TestApp",
            ModuleNamespace = "TestApp.Modules.Catalog",
            ModuleName = "Catalog",
            EntityName = "Product",
            EntityNameLower = "product",
            RouteName = "products",
        };
    }

    [Theory]
    [InlineData("mvc")]
    [InlineData("razor-pages")]
    [InlineData("blazor")]
    public async Task GenerateAsync_WithValidEngine_CreatesFiles(string engine)
    {
        var module = CreateTestModule();
        var entity = "Customer";
        var outputDir = Path.Combine(Path.GetTempPath(), $"ui-test-{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(outputDir);
            var generated = await _generator.GenerateAsync(module, entity, engine, outputDir);

            generated.Should().NotBeEmpty($"Should generate files for {engine}");

            var files = Directory.GetFiles(outputDir, "*.*", SearchOption.AllDirectories);
            files.Length.Should().BeGreaterThan(0, "Files should be written to disk");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithInvalidEngine_ReturnsEmpty()
    {
        var module = CreateTestModule();
        var entity = "Product";
        var outputDir = Path.Combine(Path.GetTempPath(), $"ui-test-{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(outputDir);
            var generated = await _generator.GenerateAsync(module, entity, "invalid-engine", outputDir);

            generated.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
