namespace Modulus.Cli.Services;

/// <summary>
/// Model passed to Scriban templates when generating an application.
/// </summary>
internal sealed class AppModel
{
    /// <summary>Full root namespace, e.g. "MyCompany.MyApp".</summary>
    public string RootNamespace { get; set; } = "";

    /// <summary>Last segment of the namespace, e.g. "MyApp".</summary>
    public string AppName { get; set; } = "";

    /// <summary>When true, skip generating the example Products module.</summary>
    public bool NoExample { get; set; }

    /// <summary>"SQLite", "SqlServer", "PostgreSQL", or "MySQL".</summary>
    public string DbProvider { get; set; } = "SQLite";

    /// <summary>EF Core package name for the provider.</summary>
    public string EfProviderPackage => DbProvider switch
    {
        "SqlServer" => "Microsoft.EntityFrameworkCore.SqlServer",
        "PostgreSQL" => "Npgsql.EntityFrameworkCore.PostgreSQL",
        "MySQL" => "MySql.EntityFrameworkCore",
        _ => "Microsoft.EntityFrameworkCore.Sqlite",
    };

    /// <summary>EF Core package version for the provider.</summary>
    public string EfProviderVersion => DbProvider switch
    {
        "SqlServer" => "10.0.9",
        "PostgreSQL" => "10.0.2",
        "MySQL" => "10.0.7",
        _ => "10.0.9",
    };

    /// <summary>Connection string for the provider.</summary>
    public string ConnectionString => DbProvider switch
    {
        "SqlServer" => "Server=localhost;Database=modulus_app;Trusted_Connection=True;TrustServerCertificate=True",
        "PostgreSQL" => "Host=localhost;Database=modulus_app;Username=postgres;Password=postgres",
        "MySQL" => "Server=localhost;Database=modulus_app;User=root;Password=root",
        _ => "Data Source=modulus_app.db",
    };

    /// <summary>EF Core DbContextOptions extension method name.</summary>
    public string UseDbMethod => DbProvider switch
    {
        "SqlServer" => "UseSqlServer",
        "PostgreSQL" => "UseNpgsql",
        "MySQL" => "UseMySql",
        _ => "UseSqlite",
    };
}

/// <summary>
/// Model for generating a module project.
/// </summary>
internal sealed class ModuleModel
{
    public string RootNamespace { get; set; } = "";
    public string ModuleName { get; set; } = "";        // "Products"
    public string ModuleNamespace { get; set; } = "";   // "MyApp.Modules.Products"
    public string ModuleProject { get; set; } = "";     // "MyApp.Modules.Products.csproj"
}

/// <summary>
/// Model for CRUD generation.
/// </summary>
internal sealed class CrudModel
{
    public string RootNamespace { get; set; } = "";
    public string ModuleNamespace { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string EntityName { get; set; } = "";        // "Product"
    public string EntityNameLower { get; set; } = "";   // "product"
    public string RouteName { get; set; } = "";         // "products"
}
