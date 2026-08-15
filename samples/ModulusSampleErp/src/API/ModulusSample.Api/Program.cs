using System.Reflection;
using ModulusSample.Api;
using ModulusSample.Api.Extensions;
using ModulusSample.Api.Middleware;
using ModulusSample.Api.Modules;
using ModulusSample.Api.OpenTelemetry;
using ModulusSample.Modules.Identity.Infrastructure;
using ModulusSample.Shared.Application;
using ModulusSample.Shared.Infrastructure;
using ModulusSample.Shared.Infrastructure.Authentication;
using ModulusSample.Shared.Infrastructure.Extensions;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Endpoints;
using Modulus.AspNetCore.Extensions;
using Modulus.AspNetCore.Cors;
using Modulus.AspNetCore.Correlation;
using Modulus.AspNetCore.Idempotency;
using Modulus.AspNetCore.RateLimiting;
using Modulus.AspNetCore.Security;
using Modulus.AspNetCore.Versioning;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Management;
using Modulus.Caching;
using Modulus.EventBus.Kafka.Extensions;
using Modulus.EventBus.RabbitMQ.Extensions;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Modulus.Events.Extensions;
using Modulus.Identity;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Modules.Notifications.Presentation;
using Modulus.Platform.Http;
using Modulus.Sagas.Extensions;
using Modulus.SignalR;
using Modulus.SignalR.Extensions;
using Modulus.Storage;
using Rebus.Transport.InMem;
using Serilog;
using Sentry;
using Sentry.AspNetCore;
using StackExchange.Redis;


[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ModulusSample.Modules.Identity.UnitTests")]

// ============================================
// NPJSON 8.0+ Dynamic JSON Support
// ============================================
// MUST be set before any Npgsql operations to enable writing .NET collections
// (List<T>, Dictionary<TKey,TValue>) to JSONB columns without explicit serialization.
AppContext.SetSwitch("Npgsql.EnableDynamicJson", true);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// SEC-008: Configure Kestrel request size limits
builder.WebHost.ConfigureKestrel(options =>
{
    // ── Body size ────────────────────────────────────────────────────────────
    // Global default: 10 MB. File-upload endpoints override this individually
    // via [RequestSizeLimit] or IHttpMaxRequestBodySizeFeature — keeping the
    // global limit tight prevents accidental exposure on non-upload routes.
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB

    // ── Concurrent connections ────────────────────────────────────────────────
    // Cap total open connections to protect against slow-loris and connection
    // exhaustion. Tune to: (available_RAM_MB / ~50 KB per idle connection).
    // 5,000 is safe on a 512 MB VPS; raise to 20,000+ on dedicated hardware.
    options.Limits.MaxConcurrentConnections = 5_000;

    // Upgraded connections (WebSocket, HTTP/2) count separately.
    // SignalR hubs + order streams need headroom here.
    options.Limits.MaxConcurrentUpgradedConnections = 1_000;

    // ── Request headers ───────────────────────────────────────────────────────
    // 100 headers is already generous; real browsers send ~20. Keeps us safe
    // against header-stuffing attacks without breaking legitimate clients.
    options.Limits.MaxRequestHeaderCount = 100;

    // 16 KB is the HTTP/1.1 spec recommendation and enough for large JWTs +
    // cookies. 32 KB is unnecessarily large — a 32 KB header is almost always
    // an attack or a misconfigured client.
    options.Limits.MaxRequestHeadersTotalSize = 16 * 1024; // 16 KB

    // ── Request line ──────────────────────────────────────────────────────────
    // 8 KB covers even deeply nested API routes with long query strings.
    // Leave at 8 KB unless you have URLs that legitimately exceed this.
    options.Limits.MaxRequestLineSize = 8 * 1024; // 8 KB

    // ── Timeouts ──────────────────────────────────────────────────────────────
    // How long Kestrel waits to receive the complete request *headers* after
    // the TCP connection is opened. Short timeout kills idle port-scanners and
    // slow-loris openers quickly. 15 s is safe for all real clients.
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);

    // How long to keep an idle HTTP/1.1 keep-alive connection open.
    // 120 s matches most reverse proxies (nginx default: 75 s). Set lower
    // (e.g. 65 s) if you sit behind a load balancer to avoid race conditions
    // where the LB closes first and causes 502s.
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);

    // ── HTTP/2 flow control ───────────────────────────────────────────────────
    // Initial window size per stream and per connection. Defaults (65 KB / 128 KB)
    // are fine for APIs. Only raise if profiling shows HTTP/2 throughput bottlenecks
    // on large response payloads (e.g. file downloads).
    // options.Limits.Http2.InitialStreamWindowSize     = 128 * 1024;
    // options.Limits.Http2.InitialConnectionWindowSize = 256 * 1024;

    // Cap concurrent HTTP/2 streams per connection. Default is 100.
    // Lower to 50 on memory-constrained hosts.
    options.Limits.Http2.MaxStreamsPerConnection = 100;

    // ── HTTP/2 keep-alive (PING frames) ───────────────────────────────────────
    // Send a PING if no frames received for 30 s; abort if no PONG within 15 s.
    // This evicts zombie connections that TCP keep-alive alone may miss.
    options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
    options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(15);

    // ── Minimum data rates ────────────────────────────────────────────────────
    // Abort connections sending request bodies slower than 100 bytes/s after a
    // 5-second grace period. Kills slow-loris body attacks without affecting
    // clients on genuinely slow connections (100 B/s = a 56k modem uploading).
    // Set to null to disable (e.g. for internal admin bulk-import endpoints).
    options.Limits.MinRequestBodyDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(5));

    // Same floor for response streaming. Prevents a single slow consumer from
    // holding a response stream open and starving the thread pool.
    options.Limits.MinResponseDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(5));
});

// ============================================
// STEP 1: Host Configuration
// ============================================
builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig.ReadFrom.Configuration(context.Configuration);

    // Bridge Serilog ↔ OpenTelemetry: TraceId/SpanId are attached to every log event via
    // the "WithOpenTelemetryTraceId"/"WithOpenTelemetrySpanId" enrichers declared in the
    // Serilog:Enrich config section. This covers background jobs / startup that run
    // outside the correlation middleware.

    // Opt-in Sentry sink: only when a DSN is configured. Reuses the SDK initialized by
    // UseSentry below (InitializeSdk=false) so there is a single Sentry pipeline that
    // forwards Error-level logs as Sentry events and Info+ as breadcrumbs.
    string? sentryDsn = context.Configuration["Sentry:Dsn"];
    if (!string.IsNullOrWhiteSpace(sentryDsn))
    {
        loggerConfig.WriteTo.Sentry(o =>
        {
            o.Dsn = sentryDsn;
            o.InitializeSdk = false;
            o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
            o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
            o.SendDefaultPii = context.Configuration.GetValue<bool>("Sentry:SendDefaultPii");
        });
    }
});

// Sentry exception capturing (opt-in). Empty Dsn => no-op.
string? sentryDsnConfig = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsnConfig))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsnConfig;
        options.Environment = builder.Environment.EnvironmentName;
        options.Release = typeof(Program).Assembly.GetName().Version?.ToString();
        options.SendDefaultPii = builder.Configuration.GetValue<bool>("Sentry:SendDefaultPii");
        options.TracesSampleRate = builder.Configuration.GetValue("Sentry:TracesSampleRate", 0.0);
        options.AttachStacktrace = true;
        options.MaxBreadcrumbs = 200;
    });
}

// ============================================
// STEP 2: Load Module Configurations FIRST
// (Must be before any service that uses IOptions)
// ============================================
builder.Configuration.AddModuleConfiguration([
    "catalog",
    "partners",
    "inventory",
    "sales",
    "purchasing",
    "billing",
    "identity",
    "tenants",
    "settings",
    "features",
    "virtualfileexplorer",
    "notifications",
    "media",
], builder.Environment.EnvironmentName);

// Add environment variables AFTER module configs to ensure they override JSON values
builder.Configuration.AddEnvironmentVariables();

// ============================================
// STEP 3: Application Layer Registration
// (Mediator, domain/Integration event handlers)
// ============================================
Assembly[] moduleApplicationAssemblies =
[
    ModulusSample.Modules.Catalog.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Partners.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Inventory.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Sales.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Purchasing.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Billing.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Identity.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Settings.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Tenants.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Features.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.VirtualFileExplorer.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Notifications.Application.AssemblyReference.Assembly,
];

builder.Services.AddApplication(moduleApplicationAssemblies);

// ModuleDbContext derives from the framework's ModuleDbContext, whose constructor
// requires DomainEventDispatcher and IModuleBus. AddModulusEvents registers them
// (plus the integration-event registry + InProcessModuleBus). No handler assemblies
// are passed here — each module composition root registers its own handlers, keeping
// domain-event dispatch behavior identical to the previous plain-DbContext setup.
builder.Services.AddModulusEvents();

// ============================================
// STEP 4: Global Middleware & API Documentation
// ============================================
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// OpenTelemetry: traces + metrics with OTLP export (config-driven, see appsettings).
builder.Services.AddAppTelemetry(builder.Configuration, builder.Environment);

// Configure JSON serialization options for Minimal APIs
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Add Swagger (OpenAPI) Documentation
builder.Services.AddSwaggerDocumentation(builder.Configuration);

// API versioning (Asp.Versioning, works with minimal-API endpoints — the app's
// previous Microsoft.AspNetCore.Mvc.Versioning/.ApiExplorer package references
// were dead weight, never wired to anything, and are the wrong library family
// for a controller-free REPR app anyway).
builder.Services.AddModulusApiVersioning(builder.Configuration);

// Response compression for better performance
builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });

// Output caching for storage endpoints
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(b => b
        .Expire(TimeSpan.FromHours(1)) // Default cache duration
        .SetVaryByHost(true));

    options.AddPolicy("StorageCache", b => b
        .Expire(TimeSpan.FromHours(24)) // Cache for 24 hours
        .SetVaryByHeader("Accept-Encoding") // Vary by compression
        .Tag("Storage"));

    options.AddPolicy("OptimizeCache", b => b
        .Expire(TimeSpan.FromDays(7))
        .SetVaryByQuery("src", "w", "h", "m", "q", "webp", "fit")
        .Tag("Optimize"));
});

// ============================================
// STEP 4.5: Security Services (Modulus.AspNetCore, config-bound — see
// "SecurityHeaders"/"RateLimiting"/"Cors"/"Correlation"/"Idempotency"
// sections in appsettings.json)
// ============================================
builder.Services.AddModulusSecurityHeaders(builder.Configuration);
builder.Services.AddModulusRateLimiting(builder.Configuration);
builder.Services.AddModulusCors(builder.Configuration);
builder.Services.AddModulusCorrelation(builder.Configuration);
builder.Services.AddModulusIdempotency(builder.Configuration);

// ForwardedHeaders: Required when behind Cloudflare / reverse proxy
// so the app sees real client IPs and correct scheme (https)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ============================================
// STEP 5: Infrastructure Layer (Database, Bus, Cache)
// ============================================
string databaseConnectionString = builder.Configuration.GetConnectionString("Database")!;

// FIX: "localhost" is not a valid Redis connection string for all clients — use "localhost:6379"
string redisConnectionString = builder.Configuration.GetConnectionString("Cache") ?? "localhost:6379";

// ============================================
// STEP 5.1: Data Protection with Redis and encryption
// ============================================
// Keys are stored in Redis so they survive container restarts.
// The multiplexer is created lazily to avoid blocking startup if Redis is slow to come up.
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(() =>
    {
        // Lazy factory: called only when a key is actually needed, not at startup
        var opts = ConfigurationOptions.Parse(redisConnectionString);
        opts.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(opts).GetDatabase();
    }, "DataProtection-Keys")
    .SetApplicationName("ModulusSampleErp");

// ============================================
// STEP 5.2: Modulus Platform Services
// ============================================
// Authorization (in-memory permission grants, org hierarchy, resource policies, field security, SoD)
builder.Services.AddModulusAuthorization();

// EF-backed authorization stores, replacing the in-memory defaults registered above.
// These must be registered for the management API below to bind: its endpoints inject
// the concrete Ef*Store types, and a minimal-API parameter that is neither a route/query
// value nor a known DI service is inferred as a *body* parameter — several of those on
// one endpoint is what previously made MapModulusAuthorizationManagement throw at startup.
// The context is registered only via IDbContextFactory, so it stays out of the module
// transaction fan-out and the module migration loop (see MigrateAuthorizationStoreAsync).
builder.Services.AddEfCoreAuthorizationStores(options =>
    options.UseNpgsql(databaseConnectionString));

// Authorization management (CRUD API for permissions, orgs, placements, plans, delegations, overrides)
builder.Services.AddModulusAuthorizationManagement();

// Multi-tenancy (header-based tenant resolution)
builder.Services.AddMultiTenancy(t => t.UseHeaderResolver());

// EF-backed tenant store (swap in-memory for database-backed stores).
// Uncomment and add the appropriate EF provider package:
// builder.Services.AddEfCoreTenantStore(options =>
//     options.UseNpgsql(databaseConnectionString));

// Distributed caching (in-memory by default; swap to Redis for production).
// Uncomment and add the Cobytelabs.Modulus.Caching.Redis NuGet package:
// builder.Services.AddRedisCacheService(redisConnectionString);

// File storage (local by default; add Cobytelabs.Modulus.Storage.AzureBlobs or
// Cobytelabs.Modulus.Storage.S3 NuGet package for cloud storage).
builder.Services.AddFileStorage(builder.Configuration);
// builder.Services.AddAzureBlobFileStorage(builder.Configuration);
// builder.Services.AddS3FileStorage(builder.Configuration);

// Resilient HTTP client (retry, circuit breaker, timeout, rate limiting)
builder.Services.AddModulusHttpClient("Default");

// ============================================
// STEP 5.2.5: OpenIddict Authorization Server
// ============================================
// The dev issuer is an http:// URI so OpenIddict permits plain-HTTP token
// requests during local development (ID2083 "HTTPS required" otherwise).
// In production this URL must be the public HTTPS authority of the token server.
if (builder.Environment.IsDevelopment())
{
    const string devIssuer = "http://localhost:5016";
    builder.Services.AddModulusOpenIddict(builder.Configuration, options =>
    {
        options.SetIssuer(new Uri(devIssuer));
        options.UseAspNetCore().DisableTransportSecurityRequirement();
    });
}
else
{
    builder.Services.AddModulusOpenIddict(builder.Configuration);
}
builder.Services.AddModulusIdentityStore<IdentityDbContext>();

// Register Modulus.Identity controllers for the token/userinfo endpoints
builder.Services.AddControllers()
    .AddApplicationPart(typeof(ModulusTokenController).Assembly);

// Dual authentication: OpenIddict validation (self-issued) + external OIDC fallback
builder.Services.AddDualAuthentication(builder.Configuration);

// ============================================
// STEP 5.3: Event Bus (RabbitMQ or Kafka)
// ============================================
bool useKafka = builder.Configuration.GetValue<bool>("MessageBus:UseKafka");
if (useKafka)
{
    builder.Services.AddKafkaEventBus(sectionName: "MessageBus:Kafka");
}
else
{
    builder.Services.AddRabbitMqEventBus(sectionName: "MessageBus:RabbitMq");
}

// ============================================
// STEP 5.4: Sagas (orchestration of long-running business processes)
// ============================================
// Sagas use Rebus internally. For production, add a Rebus transport package
// (e.g. Rebus.RabbitMq, Rebus.AzureServiceBus) and call:
//   .Rebus(rebus => rebus.Transport(t => t.UseRabbitMq("amqp://...", "sagas-queue")))
// Using in-memory transport for development/testing:
builder.Services.AddModulusSagas(sagas => sagas
    .Rebus(rebus => rebus.Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "sagas-queue")))
    .HandlersFromAssemblies(typeof(Program).Assembly));

// ============================================
// STEP 6: Health Checks
// ============================================
IHealthChecksBuilder healthChecks = builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString)
    .AddRedis(redisConnectionString);

// External OIDC health check is optional — only added when the IssuerUrl is configured.
string? oidcIssuerUrl = builder.Configuration["Identity:Oidc:IssuerUrl"];
if (!string.IsNullOrWhiteSpace(oidcIssuerUrl))
{
    healthChecks.AddUrlGroup(new Uri(oidcIssuerUrl), httpMethod: HttpMethod.Get, name: "oidc-provider");
}

// ============================================
// STEP 7: Module Infrastructure Registration
// (Identity module only — other modules removed for this standalone ERP build)
// ============================================

// Modulus module system: ModulusSampleHostModule declares [DependsOn(typeof(IdentityModule))],
// so this single call discovers + configures the full module graph (equivalent to the
// previous direct `AddIdentityModule(builder.Configuration)` call).
builder.Services.AddModulus<ModulusSampleHostModule>(builder.Configuration);

WebApplication app = builder.Build();

// ============================================
// STEP 8: Command Line Argument Handling
// ============================================
// Handle --migrate flag: Run migrations only (no Authentik required)
// FIX: Changed `return 0` to `return` — top-level statements cannot mix void and int returns.
if (args.Contains("--migrate"))
{
    Console.WriteLine("Running database migrations only...");
    await app.ApplyMigrations();
    Console.WriteLine("Migrations completed successfully.");
    return; // Exit after migrations
}

// Handle --seed flag: Run migrations then seeding
if (args.Contains("--seed"))
{
    Console.WriteLine("Running database migrations...");
    await app.ApplyMigrations();
    Console.WriteLine("Migrations completed. Running data seeding...");

    Console.WriteLine("Seeding database...");
    await app.ApplySeeding();
    Console.WriteLine("Seeding completed successfully.");
    return; // Exit after seeding
}

app.UseSwaggerConfiguration(app.Configuration);

// ============================================
// STEP 9: Request Pipeline Configuration
// ============================================
// Skip migrations when running under EF Core tools (design-time)
bool isEfDesignTime = AppContext.GetData("EFDesignTime") as bool? == true;
if (!isEfDesignTime
    && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")))
{
    // Apply database migrations in development (and in Testing so the
    // Modulus.Testing in-memory SQLite databases get their schema before the
    // hosted OpenIddict seeder runs).
    await app.ApplyMigrations();
}

// ============================================
// Middleware Pipeline (Order matters!)
// ============================================
// 0. Forwarded headers (must be first so all downstream middleware sees real IP/scheme)
app.UseForwardedHeaders();

// 1. Correlation ID (first to track all requests)
app.UseModulusCorrelation();

// 1.5. Multi-tenancy: resolve a tenant from the X-Tenant-Id header when
// present; otherwise enter the explicit host scope so the "no tenant header"
// API flow reads host-scoped (TenantId = Guid.Empty) seeded data. The sample
// data is seeded under the host tenant, so the default flow must be host.
app.Use(async (context, next) =>
{
    var hasTenantHeader = context.Request.Headers.ContainsKey("X-Tenant-Id");
    if (hasTenantHeader)
    {
        await next(context);
        return;
    }

    var currentTenant = context.RequestServices.GetRequiredService<ICurrentTenant>();
    using (currentTenant.Change(null))
    {
        await next(context);
    }
});

// 2. Request logging (captures all requests with correlation ID)
app.UseSerilogRequestLogging();

// 3. Performance monitoring (tracks all request metrics)
// Note: Performance monitoring can be added via OpenTelemetry if needed

// 4. Exception handling
app.UseExceptionHandler();

// 5. Response compression
app.UseResponseCompression();

// 5.5. Output caching for storage endpoints
app.UseOutputCache();

// 6. CORS (must be before security headers so preflight gets CORS headers)
app.UseModulusCors();

// 7. Security headers (CSP, X-Frame-Options, etc.)
app.UseModulusSecurityHeaders();

// 7. Rate limiting for anonymous/public CMS endpoints
app.UseModulusRateLimiting();

// 7. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 7.6. Modulus module pipeline (validation only — the graph is already built and
// initialized by AddModulus above) + idempotency replay for mutating requests,
// scoped to the tenant/user resolved by auth above.
app.UseModulus();
app.UseModulusIdempotency();

// 7.7. SignalR hubs (real-time notifications, updates)
app.MapModuleHubs();
app.MapNotificationHub();

// 7.9. Authorization management endpoints (CRUD for grants, orgs, placements, plans,
// delegations, overrides), all guarded by the `authorization:manage` permission.
// Mounted at /api/authorization rather than /api/auth so it reads as *authorization*
// administration and stays clearly distinct from the Identity module's /auth/* endpoints,
// which are authentication.
app.MapModulusAuthorizationManagement("/api/authorization");

// 8. User context logging (handled by CorrelationIdMiddleware above; nothing extra needed)

// 9. Static files
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
}

// OpenIddict token/userinfo endpoints (Modulus.Identity controllers)
app.MapControllers();

app.MapHealthChecks("health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// API endpoints (REPR-style auto-discovery)
app.MapModulusEndpoints(
    ModulusSample.Modules.Catalog.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Partners.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Inventory.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Sales.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Purchasing.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Billing.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Identity.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Settings.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Tenants.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Features.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.VirtualFileExplorer.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Notifications.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Media.Presentation.AssemblyReference.Assembly);


// ============================================
// RUN APPLICATION
// ============================================
app.Run();
