namespace Modulus.Tenancy.Lifecycle.Tests;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Storage;

/// <summary>Files kept in memory.</summary>
internal sealed class MemoryFiles : IFileStorage
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream(Files.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException(path)));

    public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        Files[path] = copy.ToArray();
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        Files.TryRemove(path, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(Files.ContainsKey(path));

    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => throw new NotSupportedException();
}

internal sealed class FakeTenantStore : ITenantStore
{
    public HashSet<Guid> Known { get; } = [];

    public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Known.Contains(id) ? new TenantInfo(id, "company-" + id.ToString("N")[..6]) : null);

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct) => Task.FromResult<TenantInfo?>(null);
}

internal sealed class RecordingStatusWriter : ITenantStatusWriter
{
    public List<TenantStatus> Statuses { get; } = [];

    public Task<bool> SetStatusAsync(Guid id, TenantStatus status, DateTimeOffset? trialEndsAt = null, CancellationToken ct = default)
    {
        Statuses.Add(status);
        return Task.FromResult(true);
    }
}

/// <summary>A clock the test moves forward.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}

/// <summary>The ambient company, as a stack, so nested contexts restore correctly.</summary>
internal sealed class FakeCurrentTenant : ICurrentTenant
{
    private readonly Stack<(Guid? Id, bool Host)> _previous = new();

    public Guid? TenantId { get; private set; }

    public bool IsHost { get; private set; } = true;

    public string? TenantSlug => null;

    public bool IsAvailable => TenantId is not null;

    public IDisposable Change(TenantInfo? tenant)
    {
        _previous.Push((TenantId, IsHost));
        TenantId = tenant?.TenantId;
        IsHost = tenant is null;
        return new Restore(this);
    }

    private sealed class Restore(FakeCurrentTenant owner) : IDisposable
    {
        public void Dispose()
        {
            (owner.TenantId, owner.IsHost) = owner._previous.Pop();
        }
    }
}

/// <summary>What the fake modules did, shared by every scope.</summary>
internal sealed class World
{
    public List<string> Deleted { get; } = [];
    public List<(string Dataset, Guid? Tenant)> DeleteContexts { get; } = [];
    public List<string> DisabledSteps { get; } = [];
    public HashSet<string> FailOnce { get; } = [];
    public Dictionary<string, List<Row>> Rows { get; } = [];
}

internal sealed record Row(string Id, string Name, decimal Amount, DateTimeOffset At);

internal static class Async
{
    public static async IAsyncEnumerable<T> Of<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}

/// <summary>Catalog: owned products and attachments, plus global accounts (never tenant data).</summary>
internal sealed class CatalogHandler(World world, FakeCurrentTenant current) : ITenantDataHandler
{
    public const string Products = "catalog.products";
    public const string Attachments = "catalog.attachments";
    public const string Accounts = "catalog.accounts";

    public string Module => "Catalog";

    public IReadOnlyList<TenantDataset> Datasets { get; } =
    [
        new(Products, TenantDataCategory.Owned),
        new(Attachments, TenantDataCategory.Owned),
        new(Accounts, TenantDataCategory.Global),
    ];

    public async Task ExportAsync(TenantDataset dataset, ITenantExportWriter writer, CancellationToken ct)
    {
        if (dataset.Name == Attachments)
        {
            await writer.AttachFileAsync("uploads/spec.txt", ct);
            return;
        }
        await writer.WriteRecordsAsync(Async.Of(Rows(dataset.Name)), ct);
    }

    public Task<long> CountAsync(TenantDataset dataset, CancellationToken ct) => Task.FromResult<long>(Rows(dataset.Name).Count());

    public Task<long> DeleteAsync(TenantDataset dataset, CancellationToken ct)
    {
        world.DeleteContexts.Add((dataset.Name, current.TenantId));
        if (world.FailOnce.Remove(dataset.Name))
            throw new IOException("the database hiccupped");
        world.Deleted.Add(dataset.Name);
        var count = Rows(dataset.Name).Count();
        world.Rows.Remove(dataset.Name);
        return Task.FromResult<long>(count);
    }

    private IEnumerable<Row> Rows(string dataset) => world.Rows.TryGetValue(dataset, out var rows) ? rows : [];
}

/// <summary>Billing: records kept for legal periods, and audit records that must stay.</summary>
internal sealed class BillingHandler(World world, FakeCurrentTenant current) : ITenantDataHandler
{
    public const string Invoices = "billing.invoices";
    public const string Usage = "billing.usage";
    public const string Trail = "security.audit";

    public string Module => "Billing";

    public IReadOnlyList<TenantDataset> Datasets { get; } =
    [
        new(Invoices, TenantDataCategory.Retained, RetentionClass.Financial),
        new(Usage, TenantDataCategory.Retained, RetentionClass.OperationalAudit),
        new(Trail, TenantDataCategory.Audit, RetentionClass.SecurityAudit),
    ];

    public Task ExportAsync(TenantDataset dataset, ITenantExportWriter writer, CancellationToken ct)
        => writer.WriteRecordsAsync(Async.Of(Rows(dataset.Name)), ct);

    public Task<long> CountAsync(TenantDataset dataset, CancellationToken ct) => Task.FromResult<long>(Rows(dataset.Name).Count());

    public Task<long> DeleteAsync(TenantDataset dataset, CancellationToken ct)
    {
        world.DeleteContexts.Add((dataset.Name, current.TenantId));
        world.Deleted.Add(dataset.Name);
        var count = Rows(dataset.Name).Count();
        world.Rows.Remove(dataset.Name);
        return Task.FromResult<long>(count);
    }

    private IEnumerable<Row> Rows(string dataset) => world.Rows.TryGetValue(dataset, out var rows) ? rows : [];
}

internal sealed class IntegrationStep(World world) : ITenantOffboardingStep
{
    public string Name => "webhooks";

    public Task DisableAsync(CancellationToken ct)
    {
        world.DisabledSteps.Add(Name);
        return Task.CompletedTask;
    }
}

/// <summary>A host with the lifecycle wired to fakes, a manual clock, and known keys.</summary>
internal sealed class Harness : IDisposable
{
    public static readonly Guid Company = Guid.Parse("3f2c0000-0000-0000-0000-00000000c0de");

    public ServiceProvider Services { get; }
    public MemoryFiles Files { get; } = new();
    public FakeTenantStore Tenants { get; } = new();
    public RecordingStatusWriter Status { get; } = new();
    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    public World World { get; } = new();

    public Harness(bool withExportKey = true)
    {
        Tenants.Known.Add(Company);
        var settings = new Dictionary<string, string?>
        {
            ["TenantLifecycle:CertificateSigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        if (withExportKey)
            settings["TenantLifecycle:ExportEncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        Register(services);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public IConfiguration Configuration { get; }

    /// <summary>Registers the lifecycle with these fakes, so a host builder can serve the same world.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IFileStorage>(Files);
        services.AddSingleton<ITenantStore>(Tenants);
        services.AddSingleton<ITenantStatusWriter>(Status);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(World);
        services.AddScoped<FakeCurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<FakeCurrentTenant>());
        services.AddTenantLifecycle(Configuration, lifecycle => lifecycle
            .AddHandler<CatalogHandler>()
            .AddHandler<BillingHandler>()
            .AddOffboardingStep<IntegrationStep>());
    }

    public ITenantLifecycleProvider Provider() => Services.CreateScope().ServiceProvider.GetRequiredService<ITenantLifecycleProvider>();

    public ITenantLifecycleStore Store() => Services.GetRequiredService<ITenantLifecycleStore>();

    public void Seed()
    {
        Files.Files["uploads/spec.txt"] = Encoding.UTF8.GetBytes("the specification");
        World.Rows[CatalogHandler.Products] = [new("p1", "Chair", 120m, Clock.Now), new("p2", "=HYPERLINK(\"x\")", 5m, Clock.Now)];
        World.Rows[CatalogHandler.Accounts] = [new("u1", "shared login", 0m, Clock.Now)];
        World.Rows[BillingHandler.Invoices] = [new("i1", "Invoice 1", 99.5m, Clock.Now)];
        World.Rows[BillingHandler.Usage] = [new("n1", "API calls", 3m, Clock.Now)];
        World.Rows[BillingHandler.Trail] = [new("a1", "login", 0m, Clock.Now)];
    }

    public void Dispose() => Services.Dispose();
}
