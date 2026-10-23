// The permission decision against the durable (EF) grant store, timed the way a request runs it: a fresh scope, the grants read from the
// database, then the resolver. Run by hand against a database that is empty when it starts (the schema is created, never dropped), so
// it never touches an existing one. The percentiles are the D9 measurement (the 10 ms p95 objective).
//
//   dotnet run -c Release -- --connection "Host=...;Database=authz_probe;..." --grants 10000 --roles 5 --checks 5000 --concurrency 1,8,32
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

var options = Options.Parse(args);
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton(TimeProvider.System);
// Single-company installation: the host scope, which is what the grant store reads when multi-tenancy is off.
services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
services.AddScoped<ICurrentUser, NullCurrentUser>();
services.AddEfCoreAuthorizationStores(o =>
{
    o.UseNpgsql(options.Connection);
});
// 0 = no grant copy (every check reads the database); N = a principal's grants are reused for N seconds on this node.
services.Configure<Modulus.Authorization.Grants.PermissionGrantCacheOptions>(o => o.Duration = TimeSpan.FromSeconds(options.GrantCacheSeconds));

using var provider = services.BuildServiceProvider();
var registry = new PermissionRegistry();
var roles = Enumerable.Range(0, options.Roles).Select(r => $"role{r}").ToArray();
var target = "catalog:products:manage";
var user = Guid.NewGuid();

using (var setup = provider.CreateScope())
{
    var db = setup.ServiceProvider.GetRequiredService<AuthorizationStoreDbContext>();
    // EnsureCreated answers false when the database already has a schema: the probe only runs on an empty database.
    if (!await db.Database.EnsureCreatedAsync())
        throw new InvalidOperationException("The database already has a schema. Run this against an empty database.");

    var store = setup.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();
    for (var role = 0; role < roles.Length; role++)
    {
        var permissions = Enumerable.Range(0, options.Grants)
            .Where(i => i % roles.Length == role)
            .Select(i => $"module{i}:thing{i % 7}:manage")
            .ToList();
        foreach (var permission in permissions)
            registry.Add(permission, "Probe permission.");
        if (permissions.Count > 0)
            await store.GrantToRoleAsync(roles[role], permissions);
    }
    registry.Add(target, "The permission the probe checks.");
    await store.GrantToRoleAsync(roles[^1], [target]);
}

var query = new PrincipalGrantQuery(user, roles);
Console.WriteLine($"Database: {options.Connection.Split(';').FirstOrDefault(p => p.StartsWith("Host", StringComparison.OrdinalIgnoreCase))}");
Console.WriteLine($"Grants: {options.Grants} across {roles.Length} roles (+1 target). Checks per run: {options.Checks}. Grant copy: {options.GrantCacheSeconds} s.");
Console.WriteLine();

// The first check in a process pays for the model and query compilation: reported on its own.
var cold = await DecideAsync(provider, registry, query, target);
Console.WriteLine($"Cold first decision: {cold.Ms:F2} ms (granted: {cold.Granted})");
for (var i = 0; i < 200; i++)
    await DecideAsync(provider, registry, query, target);

Console.WriteLine();
Console.WriteLine("| Concurrency | Checks | p50 ms | p95 ms | p99 ms | max ms | checks/s |");
Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|");
foreach (var concurrency in options.Concurrency)
{
    var latencies = new double[options.Checks];
    var next = -1;
    var sw = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
    {
        while (true)
        {
            var index = Interlocked.Increment(ref next);
            if (index >= options.Checks)
                return;
            var decision = await DecideAsync(provider, registry, query, target);
            latencies[index] = decision.Ms;
        }
    }));
    sw.Stop();

    Array.Sort(latencies);
    Console.WriteLine($"| {concurrency} | {options.Checks} | {Percentile(latencies, 0.50):F2} | {Percentile(latencies, 0.95):F2} | {Percentile(latencies, 0.99):F2} | {latencies[^1]:F2} | {options.Checks / sw.Elapsed.TotalSeconds:F0} |");
}

static async Task<(double Ms, bool Granted)> DecideAsync(IServiceProvider root, IPermissionRegistry registry, PrincipalGrantQuery query, string target)
{
    var start = Stopwatch.GetTimestamp();
    using var scope = root.CreateScope();
    var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
    var grants = await store.GetGrantsAsync(query);
    var granted = new PermissionResolver(store, registry).Resolve(query, grants).Contains(target);
    return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, granted);
}

static double Percentile(double[] sorted, double fraction)
{
    var rank = (int)Math.Ceiling(fraction * sorted.Length) - 1;
    return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
}

internal sealed record Options(string Connection, int Grants, int Roles, int Checks, int[] Concurrency, int GrantCacheSeconds)
{
    public static Options Parse(string[] args)
    {
        string? connection = null;
        var grants = 10_000;
        var roles = 5;
        var checks = 5_000;
        var concurrency = new[] { 1, 8, 32 };
        var grantCacheSeconds = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--connection": connection = args[++i]; break;
                case "--grants": grants = int.Parse(args[++i]); break;
                case "--roles": roles = int.Parse(args[++i]); break;
                case "--checks": checks = int.Parse(args[++i]); break;
                case "--concurrency": concurrency = args[++i].Split(',').Select(int.Parse).ToArray(); break;
                case "--grant-cache-seconds": grantCacheSeconds = int.Parse(args[++i]); break;
            }
        }

        return connection is null
            ? throw new ArgumentException("Pass --connection with the database to measure.")
            : new Options(connection, grants, roles, checks, concurrency, grantCacheSeconds);
    }
}
