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
/// Each factory instance owns its own set of in-memory SQLite databases — one
/// per module <c>DbContext</c> (a unique <c>Cache=Shared</c> name per context,
/// e.g. <c>modulus-test-&lt;guid&gt;-CatalogDbContext</c>), so tests are isolated
/// and every module context gets its own schema. Per-context databases matter in
/// multi-module apps: <c>EnsureCreated</c> short-circuits when the database
/// already has tables, so sharing one database would silently skip the second
/// module's schema. A keep-alive connection is held open per database for the
/// factory's lifetime so they survive between the pooled connections the module
/// contexts open and close.
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
    private readonly string _databasePrefix = $"modulus-test-{Guid.NewGuid():N}";

    private readonly List<SqliteConnection> _keepAlives = [];

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            // Point every module DbContext at its own factory-owned SQLite
            // database. The per-context connection map is only available once
            // this callback runs (during host build), so the keep-alives are
            // opened in CreateHost after the host is built — see there.
            services.UsePerContextSqlite(_databasePrefix);

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
        // The host's startup (generated Program) runs MigrateModulusDatabasesAsync
        // while this method's base call builds it — but ConfigureTestServices (and
        // therefore the SQLite swap) executes only at that point, so the per-context
        // connection map is not available before the host exists. The schema the
        // startup just created lives in in-memory databases held only by transient
        // connections, so it is lost the moment those connections close.
        var host = base.CreateHost(builder);

        // So: resolve each module context from the built host, open a keep-alive
        // for its own database (a shared-cache in-memory DB survives only while at
        // least one connection to it stays open — otherwise it dies with the last
        // closing connection), and then recreate the schema inside those kept-alive
        // databases. The connection string comes from the context itself, so the
        // keep-alive is guaranteed to target the exact cache the context uses.
        using (var scope = host.Services.CreateScope())
        {
            foreach (var db in scope.ServiceProvider.GetServices<DbContext>())
            {
                var connectionString = db.Database.GetConnectionString();
                if (string.IsNullOrWhiteSpace(connectionString))
                    continue;

                var keepAlive = new SqliteConnection(connectionString);
                keepAlive.Open();
                _keepAlives.Add(keepAlive);
            }
        }

        // EnsureCreated is idempotent per context, so this is a no-op when the
        // app already ran MigrateModulusDatabasesAsync against these databases.
        using var safetyScope = host.Services.CreateScope();
        foreach (var db in safetyScope.ServiceProvider.GetServices<DbContext>())
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
        {
            foreach (var keepAlive in _keepAlives)
                keepAlive.Dispose();
            _keepAlives.Clear();
        }
        base.Dispose(disposing);
    }
}
