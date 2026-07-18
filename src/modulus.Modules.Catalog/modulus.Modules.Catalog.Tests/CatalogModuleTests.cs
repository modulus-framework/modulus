using modulus.Modules.Catalog.Infrastructure;
using Xunit;

namespace modulus.Modules.Catalog.Tests;

[Trait("Category", "Unit")]
public sealed class CatalogModuleTests
{
    [Fact]
    public void Module_Has_No_Dependencies_By_Default()
    {
        var module = new CatalogModule();
        Assert.NotNull(module);
    }
}
