namespace Modulus.Core.Abstractions;

/// <summary>
/// Module-level data seeder. Register as a singleton via
/// <c>services.AddSingleton&lt;IDataSeeder, MySeeder&gt;()</c>; run all
/// seeders at startup with
/// <c>await app.Services.SeedModulusDataAsync()</c>.
/// </summary>
public interface IDataSeeder
{
    /// <summary>
    /// Seeds the data required by this module. Called once at startup after
    /// all module <see cref="IModule.InitializeAsync"/> methods have run.
    /// Implementations must be idempotent — re-running after a partial
    /// failure must not create duplicates.
    /// </summary>
    Task SeedAsync(CancellationToken ct = default);
}
