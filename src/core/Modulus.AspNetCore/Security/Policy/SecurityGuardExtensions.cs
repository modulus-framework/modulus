namespace Modulus.AspNetCore.Security.Policy;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Configuration;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;

/// <summary>Registers the startup security guard.</summary>
public static class SecurityGuardExtensions
{
    /// <summary>
    /// Adds the startup security guard (section <c>Security:Guard</c>): when the host starts, after every
    /// endpoint is mapped, it resolves each endpoint's policy, logs the loosening report, and refuses to
    /// start on an unpoliced endpoint, an anonymous one without a reason, a classified anonymous one or an
    /// unenforceable network requirement, and (outside Development) a loosening missing from the
    /// allow-list. The last report is available from <see cref="SecurityGuardState"/>. It also adds the in-memory
    /// store check (<see cref="InMemoryStoreCheckExtensions.AddModulusInMemoryStoreCheck"/>).
    /// </summary>
    public static IServiceCollection AddModulusSecurityGuard(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecurityGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = services.AddOptions<SecurityGuardOptions>()
            .Bind(configuration.GetSection(SecurityGuardOptions.SectionName));
        if (configure is not null)
            options.Configure(configure);

        services.AddModulusInMemoryStoreCheck(configuration);
        services.TryAddSingleton<SecurityGuardState>();
        if (!services.Any(d => d.ImplementationType == typeof(SecurityGuardHostedService)))
            services.AddHostedService<SecurityGuardHostedService>();
        return services;
    }
}

internal sealed partial class SecurityGuardHostedService(
    IServiceProvider services,
    IOptions<SecurityGuardOptions> options,
    SecurityGuardState state,
    ILogger<SecurityGuardHostedService> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // After every StartAsync: the web host registers its server last, and only while starting it builds the
    // pipeline that hands the mapped endpoints to routing, so earlier the EndpointDataSource is still empty.
    // Throwing here still fails Host.StartAsync, so the application does not run.
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        if (services.GetService<EndpointDataSource>() is not { } dataSource)
            return;

        var environment = services.GetService<IWebHostEnvironment>();
        var allowList = LooseningAllowList.Load(ResolvePath(settings.AllowListPath, environment?.ContentRootPath));
        var fallback = services.GetService<IAuthorizationPolicyProvider>() is { } provider
            && await provider.GetFallbackPolicyAsync().ConfigureAwait(false) is not null;

        var report = EndpointSecurityAnalyzer.Analyze(dataSource.Endpoints, fallback, allowList) with
        {
            Surfaces = services.GetServices<ISecuritySurfaceContributor>()
                .SelectMany(c => c.Describe(services))
                .OrderBy(e => e.Surface, StringComparer.Ordinal)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList(),
        };
        state.Report = report;

        foreach (var entry in report.Loosened)
            LogLoosened(entry.Methods.Count > 0 ? string.Join(',', entry.Methods) : "*", entry.Route, entry.LooseningReason, entry.Ticket ?? "-");
        foreach (var item in report.Surfaces.Where(e => e.Access == SecuritySurfaceAccess.Anonymous))
            LogAnonymousSurface(item.Surface, item.Name);
        LogSummary(report.Endpoints.Count, report.Loosened.Count(), report.Findings.Count);
        if (report.Surfaces.Count > 0)
        {
            LogSurfaceSummary(
                report.Surfaces.Count,
                report.Surfaces.Count(e => e.Access == SecuritySurfaceAccess.Policed),
                report.Surfaces.Count(e => e.Access == SecuritySurfaceAccess.Anonymous));
        }

        var development = environment?.IsDevelopment() == true;
        var failing = report.Findings
            .Where(f => f.Severity == SecurityGuardSeverity.Error || !development || settings.FailOnUnlistedInDevelopment)
            .ToList();

        foreach (var finding in report.Findings.Except(failing))
            LogWarning(finding.Code, finding.Route, finding.Message);

        RecordInAudit(report, failing.Count > 0);

        if (failing.Count > 0)
            throw new SecurityGuardException(failing);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The anonymous surface goes into the host's security audit chain at every start: the fingerprint changes
    // whenever an endpoint is opened or closed, so a loosening shipped without review shows up in the audit.
    private void RecordInAudit(SecurityGuardReport report, bool refused)
    {
        if (services.GetService<ISecurityAuditLog>() is not { } audit)
            return;

        var loosened = report.Loosened
            .Select(e => $"{(e.Methods.Count > 0 ? string.Join(',', e.Methods) : "*")} {e.Route}")
            .Concat(report.Surfaces.Where(e => e.Access == SecuritySurfaceAccess.Anonymous).Select(e => $"{e.Surface} {e.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();
        audit.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Configuration,
            Action = "startup.loosening-report",
            Outcome = refused ? SecurityAuditOutcomes.Denied : SecurityAuditOutcomes.Success,
            Target = services.GetService<IWebHostEnvironment>()?.ApplicationName,
            Details = new Dictionary<string, string?>
            {
                ["endpoints"] = report.Endpoints.Count.ToString(CultureInfo.InvariantCulture),
                ["anonymous"] = loosened.Count.ToString(CultureInfo.InvariantCulture),
                ["findings"] = report.Findings.Count.ToString(CultureInfo.InvariantCulture),
                ["fingerprint"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', loosened)))),
                ["routes"] = string.Join("; ", loosened),
            },
        });
    }

    private static string ResolvePath(string path, string? contentRoot)
        => Path.IsPathRooted(path) || string.IsNullOrEmpty(contentRoot) ? path : Path.Combine(contentRoot, path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security guard: anonymous {Methods} {Route}: {Reason} (ticket: {Ticket})")]
    private partial void LogLoosened(string methods, string route, string? reason, string ticket);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security guard: {Endpoints} endpoints checked, {Loosened} anonymous, {Findings} findings")]
    private partial void LogSummary(int endpoints, int loosened, int findings);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security guard: anonymous {Surface} {Name}")]
    private partial void LogAnonymousSurface(string surface, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Security guard: {Items} GraphQL fields / realtime topics, {Policed} with their own policy, {Anonymous} anonymous")]
    private partial void LogSurfaceSummary(int items, int policed, int anonymous);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security guard [{Code}] {Route}: {Message} (allowed in Development)")]
    private partial void LogWarning(string code, string route, string message);
}
