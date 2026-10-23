using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// Keyset (cursor) paging in generated modules: generated only where the framework can order a Guid v7 key by position
/// (PostgreSQL on .NET 10), and offset paging everywhere else.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CursorPagingTemplateTests
{
    private static ModuleModel Module(string provider, string framework) => new()
    {
        RootNamespace = "Shop",
        ModuleName = "Catalog",
        ModuleNamespace = "Shop.Modules.Catalog",
        EntityName = "Product",
        RouteName = "products",
        DbProvider = provider,
        TargetFramework = framework,
    };

    [Theory]
    [InlineData("PostgreSQL", "net10.0", true)]
    [InlineData("PostgreSQL", "net8.0", false)]
    [InlineData("SQLite", "net10.0", false)]
    [InlineData("SqlServer", "net10.0", false)]
    [InlineData("MySQL", "net10.0", false)]
    public void Cursor_paging_is_generated_only_where_the_framework_can_seek_a_guid_v7_key(string provider, string framework, bool expected)
        => Module(provider, framework).CursorPaging.Should().Be(expected);

    [Fact]
    public void The_cursor_query_handler_and_endpoint_render_for_postgresql_on_net10()
    {
        var engine = new TemplateEngine();
        var model = Module("PostgreSQL", "net10.0");

        engine.Render("module/Application/GetCursorQuery", model).Should().Contain("CursorQuery(string? Cursor");
        engine.Render("module/Application/GetCursorHandler", model)
            .Should().Contain("ValidationException")
            .And.Contain("GetKeysetPageAsync");
        engine.Render("module/Presentation/CursorEndpoint", model).Should().Contain("/api/catalog/products/cursor");
    }

    [Fact]
    public void The_repository_carries_the_keyset_method_only_where_it_is_enabled()
    {
        var engine = new TemplateEngine();

        engine.Render("module/Domain/IRepository", Module("PostgreSQL", "net10.0")).Should().Contain("GetKeysetPageAsync");
        engine.Render("module/Domain/IRepository", Module("SQLite", "net8.0")).Should().NotContain("GetKeysetPageAsync");

        var enabled = engine.Render("module/Infrastructure/Repository", Module("PostgreSQL", "net10.0"));
        enabled.Should().Contain("IReadRepository<Product> reads")
            .And.Contain("reads.ListKeysetAsync(");
        var disabled = engine.Render("module/Infrastructure/Repository", Module("SQLite", "net8.0"));
        disabled.Should().NotContain("reads.ListKeysetAsync(").And.NotContain("CursorCodec");
    }
}
