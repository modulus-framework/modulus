namespace Modulus.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
/// A keep-alive connection is opened for each database the moment its context's options are first built, and the schema is
/// (re)created by a hosted service registered <b>before</b> the application's own: a shared-cache in-memory database dies the
/// moment its last connection closes, so a host that migrates and seeds in <c>Program.cs</c> (before any hosted service starts)
/// needs the first, and startup seeders that run during <c>StartAsync</c> need the second.
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
            // endpoints accept CreateAuthenticatedClient's principal. An app that
            // set its own defaults (a token server's validation scheme, a cookie/bearer
            // policy scheme) would otherwise keep authenticating with them, so the
            // defaults are overridden after every registration, PostConfigure included.
            services.AddAuthentication(TestAuthDefaults.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthDefaults.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthDefaults.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthDefaults.SchemeName;
                options.DefaultChallengeScheme = TestAuthDefaults.SchemeName;
                options.DefaultForbidScheme = TestAuthDefaults.SchemeName;
            });

            // Keep-alive + EnsureCreated for every swapped database, running
            // BEFORE the application's own hosted services (index 0) so startup
            // seeders see live schema. Scoped/module contexts resolve through
            // DI; factory-only contexts (e.g. AddEfCoreAuthorizationStores)
            // resolve through their IDbContextFactory<T>.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(sp =>
                new SqliteKeepAliveService(sp, _registry)));
        });
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
            _registry.Dispose();
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
        TestDatabaseRegistry registry) : IHostedService
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

            // Normally already open (the options hook did it when the context was first built); this covers the rest.
            registry.KeepAlive(connectionString);

            // EnsureCreated is idempotent per context; any schema the app
            // created while wiring the host died with its connections.
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
