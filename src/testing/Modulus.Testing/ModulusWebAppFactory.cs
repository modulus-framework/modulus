namespace Modulus.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
/// The keep-alives are opened by a hosted service registered <b>before</b> the
/// application's own hosted services: startup seeders (OpenIddict clients,
/// background jobs, …) hit the database during <c>StartAsync</c>, and a
/// shared-cache in-memory database dies the moment its last connection closes —
/// so by the time any startup hosted service runs, the schema created while the
/// host was being wired has already evaporated. Running first fixes that.
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

    private readonly TestDatabaseRegistry _registry = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            // Point every module DbContext at its own factory-owned SQLite
            // database; record which contexts are factory-registered so the
            // keep-alive pass below can reach those too.
            services.UsePerContextSqlite(_databasePrefix, _registry);

            // …and make the header-driven test scheme the default so [Authorize]
            // endpoints accept CreateAuthenticatedClient's principal.
            services.AddAuthentication(TestAuthDefaults.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthDefaults.SchemeName, _ => { });

            // Keep-alive + EnsureCreated for every swapped database, running
            // BEFORE the application's own hosted services (index 0) so startup
            // seeders see live schema. Scoped/module contexts resolve through
            // DI; factory-only contexts (e.g. AddEfCoreAuthorizationStores)
            // resolve through their IDbContextFactory<T>.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(sp =>
                new SqliteKeepAliveService(sp, _registry, HoldConnection)));
        });
    }

    private void HoldConnection(SqliteConnection connection) => _keepAlives.Add(connection);

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

    /// <summary>
    /// Opens one keep-alive connection per swapped database and creates its
    /// schema, before the application's hosted services start. A shared-cache
    /// in-memory SQLite database only lives while at least one connection to
    /// it is open, and connections opened during host wiring (startup
    /// migrations) close before <c>StartAsync</c> — without this service the
    /// first startup seeder would find an empty database.
    /// </summary>
    private sealed class SqliteKeepAliveService(
        IServiceProvider provider,
        TestDatabaseRegistry registry,
        Action<SqliteConnection> holdConnection) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var scope = provider.CreateScope())
            {
                foreach (var db in scope.ServiceProvider.GetServices<DbContext>())
                    await KeepAliveAsync(db, cancellationToken);
            }

            foreach (var contextType in registry.FactoryContextTypes)
            {
                var factoryType = typeof(IDbContextFactory<>).MakeGenericType(contextType);
                var createAsync = factoryType.GetMethod(
                    "CreateDbContextAsync", [typeof(CancellationToken)])!;
                var factory = provider.GetRequiredService(factoryType);
                var task = (Task)createAsync.Invoke(factory, [cancellationToken])!;
                await task;
                var db = (DbContext)task.GetType().GetProperty("Result")!.GetValue(task)!;
                await KeepAliveAsync(db, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task KeepAliveAsync(DbContext db, CancellationToken cancellationToken)
        {
            var connectionString = db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return;

            var keepAlive = new SqliteConnection(connectionString);
            await keepAlive.OpenAsync(cancellationToken);
            holdConnection(keepAlive);

            // EnsureCreated is idempotent per context; any schema the app
            // created while wiring the host died with its connections.
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
