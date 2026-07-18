namespace Modulus.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Testing.Internal;

/// <summary>
/// Boots a Modulus application's real host — every middleware, the mediator
/// pipeline, all module DbContexts — against a throwaway database, so module
/// authors can drive endpoints over HTTP without hand-rolling
/// <see cref="WebApplicationFactory{TEntryPoint}"/> plumbing.
/// </summary>
/// <remarks>
/// <para>
/// Each factory instance owns a private in-memory SQLite database (a unique
/// <c>Cache=Shared</c> name), so tests are isolated: a second factory never sees
/// the first's data. A single keep-alive connection is held open for the
/// factory's lifetime so the database survives between the pooled connections the
/// module contexts open and close.
/// </para>
/// <para>
/// The host runs in the <c>Testing</c> environment. Register per-test overrides
/// (a fixed <see cref="TimeProvider"/>, stub services, seed data) by subclassing
/// and overriding <see cref="ConfigureWebHost"/> with a further
/// <c>ConfigureTestServices</c> call — yours runs after the base wiring.
/// </para>
/// <para><typeparamref name="TEntryPoint"/> is the application's entry-point class
/// (its <c>Program</c>). Generated apps expose it via
/// <c>public partial class Program;</c>.</para>
/// </remarks>
public class ModulusWebAppFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private readonly string _connectionString =
        $"Data Source=modulus-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    private SqliteConnection? _keepAlive;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            // Point every module DbContext at this factory's SQLite database…
            services.UseSharedSqlite(_connectionString);

            // …and make the header-driven test scheme the default so [Authorize]
            // endpoints accept CreateAuthenticatedClient's principal.
            services.AddAuthentication(TestAuthDefaults.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthDefaults.SchemeName, _ => { });
        });
    }

    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Open the keep-alive BEFORE the host boots: a generated Program runs
        // MigrateModulusDatabasesAsync (EnsureCreated) during startup, and the
        // shared in-memory database must already exist by then.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        var host = base.CreateHost(builder);

        // Safety net: create the schema for hosts that don't initialise it
        // themselves. EnsureCreated is idempotent, so this is a no-op when the
        // app already ran MigrateModulusDatabasesAsync.
        using var scope = host.Services.CreateScope();
        foreach (var db in scope.ServiceProvider.GetServices<DbContext>())
            db.Database.EnsureCreated();

        return host;
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> whose requests carry a test principal.
    /// Endpoints see an authenticated <c>ClaimsPrincipal</c> (and a
    /// <c>ClaimsPrincipal</c>-based <c>ICurrentUser</c> resolves this identity).
    /// </summary>
    /// <param name="userId">User id claim; a random <see cref="Guid"/> when omitted.</param>
    /// <param name="userName">User name claim; <c>test-user</c> when omitted.</param>
    /// <param name="email">Optional email claim.</param>
    /// <param name="roles">Optional roles (each becomes a role claim).</param>
    /// <param name="permissions">Optional permissions (each becomes a <c>permission</c> claim).</param>
    /// <param name="tenantId">Optional tenant id, sent as the <c>X-Tenant-Id</c>
    /// header; requires the app's tenant store to resolve it.</param>
    public HttpClient CreateAuthenticatedClient(
        Guid? userId = null,
        string? userName = null,
        string? email = null,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        Guid? tenantId = null)
    {
        var client = CreateClient();
        var headers = client.DefaultRequestHeaders;

        headers.Add(TestAuthDefaults.UserIdHeader, (userId ?? Guid.NewGuid()).ToString());
        headers.Add(TestAuthDefaults.UserNameHeader, userName ?? "test-user");

        if (!string.IsNullOrEmpty(email))
            headers.Add(TestAuthDefaults.EmailHeader, email);
        if (roles is not null)
            headers.Add(TestAuthDefaults.RolesHeader, string.Join(',', roles));
        if (permissions is not null)
            headers.Add(TestAuthDefaults.PermissionsHeader, string.Join(',', permissions));
        if (tenantId is not null)
            headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        return client;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _keepAlive?.Dispose();
        base.Dispose(disposing);
    }
}
