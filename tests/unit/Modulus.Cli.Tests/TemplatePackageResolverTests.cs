using FluentAssertions;
using Modulus.Cli.Services;
using System.Diagnostics;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class TemplatePackageResolverTests
{
    private readonly ITemplatePackageResolver _resolver = new TemplatePackageResolver();

    [Fact]
    public async Task ResolveAsync_WithValidPackage_ReturnsCachePath()
    {
        var packageId = "Modulus.Ui.Templates";
        var version = "0.9.0";
        
        var result = await _resolver.ResolveAsync(packageId, version);
        
        result.Should().NotBeNull();
        Directory.Exists(result).Should().BeTrue();
        result.Should().Contain("templates");
    }

    [Fact]
    public async Task ResolveAsync_WithCachedPackage_UsesCache()
    {
        var packageId = "Modulus.Ui.Templates";
        var version = "0.9.0";
        
        var sw = Stopwatch.StartNew();
        var result1 = await _resolver.ResolveAsync(packageId, version);
        sw.Stop();
        var time1 = sw.ElapsedMilliseconds;
        
        result1.Should().NotBeNull();
        
        sw.Restart();
        var result2 = await _resolver.ResolveAsync(packageId, version);
        sw.Stop();
        var time2 = sw.ElapsedMilliseconds;
        
        result1.Should().Be(result2);
        time2.Should().BeLessThan(time1 / 3);
    }

    [Fact]
    public async Task ValidateTemplatesAsync_WithValidStructure_ReturnsTrue()
    {
        var packageId = "Modulus.Ui.Templates";
        var version = "0.9.0";
        var templatesPath = await _resolver.ResolveAsync(packageId, version);
        
        templatesPath.Should().NotBeNull();
        var isValid = await _resolver.ValidateTemplatesAsync(templatesPath!);
        
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WithInvalidVersion_ReturnsNull()
    {
        var packageId = "Modulus.Ui.Templates";
        var invalidVersion = "99.99.99";
        
        var result = await _resolver.ResolveAsync(packageId, invalidVersion);
        
        result.Should().BeNull();
    }
}
