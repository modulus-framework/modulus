namespace Modulus.Bff;

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Modulus.Bff.Http;
using Modulus.Caching;

/// <summary>A composed response: the shaped payload plus the optional sections that failed.</summary>
public sealed record BffResponse<T>(T Data, IReadOnlyList<string> Degraded);

/// <summary>Per-section settings.</summary>
public sealed class BffSectionOptions
{
    /// <summary>The section's own deadline; it fails (or degrades, when optional) after it.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Caches the section's value (FusionCache) for this long. <c>null</c> = not cached.</summary>
    public TimeSpan? CacheDuration { get; set; }

    /// <summary>Cache per signed-in user (the default) or share the value across users of the client.</summary>
    public bool CachePerUser { get; set; } = true;

    /// <summary>Overrides the cache key (default: the section name); always scoped by client, selected company (and user).</summary>
    public string? CacheKey { get; set; }

    /// <summary>Cache tags, e.g. the module's <c>catalog:products</c> tag so writes evict it.</summary>
    public string[]? CacheTags { get; set; }
}

/// <summary>One section of a composition; read <see cref="Value"/> after <see cref="BffComposition.ExecuteAsync"/>.</summary>
public sealed class BffSection<T>
{
    internal BffSection(string name, bool required) => (Name, Required) = (name, required);

    public string Name { get; }

    public bool Required { get; }

    /// <summary>True when the section produced a value.</summary>
    public bool Succeeded { get; internal set; }

    /// <summary>The value, or <c>default</c> when an optional section degraded.</summary>
    public T? Value { get; internal set; }
}

/// <summary>Thrown when a required section fails; the BFF answers <c>502</c>.</summary>
public sealed class BffSectionFailedException(string section, Exception inner)
    : Exception($"The required BFF section '{section}' failed.", inner)
{
    public string Section { get; } = section;
}

/// <summary>
/// Fans out to upstream services for one client-shaped response. Sections run concurrently, each
/// with its own timeout; a failing <b>optional</b> section yields <c>default</c> and its name in
/// <see cref="Degraded"/> instead of failing the whole response (the usual microservice case: one
/// service is down, the screen still renders). A failing <b>required</b> section throws
/// <see cref="BffSectionFailedException"/>. Sections can be cached through <see cref="ICacheService"/>.
/// </summary>
public sealed class BffComposition
{
    private readonly List<Func<CancellationToken, Task>> _runs = [];
    private readonly List<string> _degraded = [];
    private readonly ICacheService? _cache;
    private readonly IBffClientContext _client;
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger _logger;
    private readonly TimeSpan? _defaultTimeout;
    private readonly string _tenantHeader;

    internal BffComposition(
        ICacheService? cache, IBffClientContext client, IHttpContextAccessor accessor, ILogger logger, TimeSpan? defaultTimeout, string tenantHeader)
        => (_cache, _client, _accessor, _logger, _defaultTimeout, _tenantHeader) = (cache, client, accessor, logger, defaultTimeout, tenantHeader);

    /// <summary>The optional sections that failed or timed out.</summary>
    public IReadOnlyList<string> Degraded => _degraded;

    /// <summary>Adds a section whose failure fails the response.</summary>
    public BffSection<T> Required<T>(string name, Func<CancellationToken, Task<T>> factory, BffSectionOptions? options = null)
        => Add(name, required: true, factory, options);

    /// <summary>Adds a section whose failure degrades the response.</summary>
    public BffSection<T> Optional<T>(string name, Func<CancellationToken, Task<T>> factory, BffSectionOptions? options = null)
        => Add(name, required: false, factory, options);

    /// <summary>Runs every section concurrently.</summary>
    public async Task ExecuteAsync(CancellationToken ct = default)
        => await Task.WhenAll(_runs.Select(run => run(ct))).ConfigureAwait(false);

    /// <summary>Runs every section, then shapes the response with <paramref name="shape"/>.</summary>
    public async Task<BffResponse<TResult>> ExecuteAsync<TResult>(Func<TResult> shape, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(shape);
        await ExecuteAsync(ct).ConfigureAwait(false);
        return new BffResponse<TResult>(shape(), _degraded.ToArray());
    }

    private BffSection<T> Add<T>(string name, bool required, Func<CancellationToken, Task<T>> factory, BffSectionOptions? options)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        var section = new BffSection<T>(name, required);
        options ??= new BffSectionOptions();
        _runs.Add(ct => RunAsync(section, factory, options, ct));
        return section;
    }

    private async Task RunAsync<T>(BffSection<T> section, Func<CancellationToken, Task<T>> factory, BffSectionOptions options, CancellationToken ct)
    {
        using var activity = BffActivity.Source.StartActivity("bff.section " + section.Name);
        activity?.SetTag("bff.section", section.Name);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if ((options.Timeout ?? _defaultTimeout) is { } timeout)
            cts.CancelAfter(timeout);

        try
        {
            section.Value = options.CacheDuration is { } duration && _cache is not null
                ? await _cache.GetOrCreateAsync(CacheKey(section.Name, options), factory, duration, options.CacheTags, cts.Token).ConfigureAwait(false)
                : await factory(cts.Token).ConfigureAwait(false);
            section.Succeeded = true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (section.Required)
                throw new BffSectionFailedException(section.Name, ex);

            _logger.LogWarning(ex, "Optional BFF section {Section} degraded", section.Name);
            lock (_degraded)
                _degraded.Add(section.Name);
        }
    }

    // The selected company is part of every key: one login reaches several companies, and a section cached
    // for company A must never answer a request made in company B (the cache service's own tenant scoping does
    // not help here, because a BFF host resolves no tenant).
    private string CacheKey(string section, BffSectionOptions options)
    {
        var tenant = BffTenant.Selected(_accessor.HttpContext, _tenantHeader);
        var key = "bff:section:" + (_client.Name ?? "-") + ":"
            + (tenant is null ? "-" : Uri.EscapeDataString(tenant)) + ":" + (options.CacheKey ?? section);
        if (!options.CachePerUser)
            return key;
        var user = _accessor.HttpContext?.User;
        var subject = user?.FindFirst("sub")?.Value ?? user?.Identity?.Name ?? "anonymous";
        return key + ":" + subject;
    }
}

/// <summary>Creates <see cref="BffComposition"/>s for BFF endpoints.</summary>
public sealed class BffComposer(IServiceProvider services, IBffClientContext client, IHttpContextAccessor accessor, ILogger<BffComposer> logger)
{
    /// <summary>Starts a composition; <paramref name="defaultTimeout"/> applies to sections without their own.</summary>
    public BffComposition Begin(TimeSpan? defaultTimeout = null)
        => new(services.GetService<ICacheService>(), client, accessor, logger, defaultTimeout,
            services.GetService<IOptions<BffOptions>>()?.Value.TenantHeader ?? BffDefaults.TenantHeader);
}

internal static class BffActivity
{
    public static readonly ActivitySource Source = new("Modulus.Bff");
}
