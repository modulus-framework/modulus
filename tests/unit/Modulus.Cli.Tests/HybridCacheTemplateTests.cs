using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class HybridCacheTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(string caching) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        DbProvider = "SQLite",
        Auth = "none",
        CachingProvider = caching,
    };

    [Fact]
    public void Inmemory_host_registers_the_L1_fusion_cache()
    {
        var program = _engine.Render("app/Program", Model("inmemory"));
        var csproj = _engine.Render("app/api.csproj", Model("inmemory"));

        program.Should().Contain("builder.Services.AddModulusFusionCache(builder.Configuration);");
        program.Should().NotContain("AddRedisFusionCache(builder.Configuration)");
        program.Should().Contain("using Modulus.Caching;");
        csproj.Should().Contain("Cobytelabs.Modulus.Platform");
        csproj.Should().NotContain("Cobytelabs.Modulus.Caching.Redis");
    }

    [Fact]
    public void Redis_host_registers_redis_L2_and_backplane()
    {
        var program = _engine.Render("app/Program", Model("redis"));
        var csproj = _engine.Render("app/api.csproj", Model("redis"));

        program.Should().Contain("builder.Services.AddRedisFusionCache(builder.Configuration);");
        program.Should().NotContain("builder.Services.AddModulusFusionCache(builder.Configuration);");
        csproj.Should().Contain("Cobytelabs.Modulus.Caching.Redis");
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData("redis")]
    public void Appsettings_seed_the_cache_section_as_valid_json(string caching)
    {
        var json = _engine.Render("app/appsettings.json", Model(caching));

        using var doc = JsonDocument.Parse(json);
        var caching_ = doc.RootElement.GetProperty("Caching");
        caching_.GetProperty("Fusion").GetProperty("CacheName").GetString().Should().Be("shop");
        caching_.TryGetProperty("Redis", out _).Should().Be(caching == "redis");
    }

    [Fact]
    public void Generated_queries_are_cached_and_commands_invalidate_the_same_tag()
    {
        var model = Model("inmemory");
        const string tag = "catalog:products";

        _engine.Render("module/Application/GetAllQuery", model).Should().Contain($"[CacheFor(30, Tags = [\"{tag}\"])]");
        _engine.Render("module/Application/GetByIdQuery", model).Should().Contain($"[CacheFor(30, Tags = [\"{tag}\"])]");
        foreach (var command in new[] { "CreateCommand", "UpdateCommand", "DeleteCommand" })
            _engine.Render($"module/Application/{command}", model).Should().Contain($"[InvalidatesCache(\"{tag}\")]");
    }
}
