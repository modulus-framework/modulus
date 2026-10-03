namespace Modulus.Testing.Security;

using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Probes every endpoint of a host booted by <see cref="ModulusWebAppFactory{TEntryPoint}"/> the way an attacker
/// would, so a new endpoint that forgot its policy fails the build instead of shipping (security plan, phase 5):
/// <list type="bullet">
/// <item><b>anonymous</b>: every endpoint that is not explicitly loosened answers <c>401</c> (or a redirect to
/// a login page) to a caller without credentials;</item>
/// <item><b>no-permission</b>: every endpoint that requires a policy, permission or role answers <c>403</c> to a
/// signed-in caller who holds none;</item>
/// <item><b>foreign-tenant</b> (when <see cref="SecurityProbeOptions.ForeignTenantId"/> is set): a caller holding
/// the endpoint's permissions but no membership in that company gets <c>403</c> when selecting it.</item>
/// </list>
/// </summary>
/// <remarks>
/// Route parameters are filled with placeholders (<see cref="SecurityProbeOptions.RouteValue"/>), and unsafe
/// methods send an empty JSON object: authorization runs before model binding, so the body never matters for the
/// expected answers. Responses are read up to their headers only, so event streams do not hang the suite.
/// </remarks>
public static class SecurityProbeSuite
{
    /// <summary>Runs every probe and returns the report (<see cref="SecurityProbeReport.EnsureNoFailures"/> fails a test).</summary>
    public static async Task<SecurityProbeReport> RunAsync<TEntryPoint>(
        ModulusWebAppFactory<TEntryPoint> factory, SecurityProbeOptions? options = null, CancellationToken ct = default)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        return await RunAsync(factory.Services, client, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs every probe against a host whose endpoints are in <paramref name="services"/> and which authenticates
    /// with <see cref="TestAuthHandler"/> (the probe sets its <c>X-Test-*</c> headers per request).
    /// <paramref name="client"/> must not follow redirects.
    /// </summary>
    public static async Task<SecurityProbeReport> RunAsync(
        IServiceProvider services, HttpClient client, SecurityProbeOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(client);
        options ??= new SecurityProbeOptions();

        var results = new List<SecurityProbeResult>();
        var loosened = new List<string>();
        foreach (var target in Discover(services, options))
        {
            if (target.Anonymous)
            {
                loosened.Add($"{target.Method} {target.Route}");
                continue;
            }

            var status = await SendAsync(client, target, caller: null, options, ct).ConfigureAwait(false);
            var accepted = options.AnonymousStatusOverrides.TryGetValue(target.Route, out var codes) ? codes : [401];
            results.Add(Result(target, SecurityProbe.Anonymous, string.Join('/', accepted), status,
                accepted.Contains((int)status.Code) || options.IsLoginRedirect(status)));

            if (target.RequiresMoreThanSignIn)
            {
                status = await SendAsync(client, target, new Caller([], [], null), options, ct).ConfigureAwait(false);
                results.Add(Result(target, SecurityProbe.NoPermission, "403", status, status.Code == HttpStatusCode.Forbidden));
            }

            if (options.ForeignTenantId is { } foreign)
            {
                status = await SendAsync(client, target, new Caller(target.Roles, target.Policies, foreign), options, ct).ConfigureAwait(false);
                results.Add(Result(target, SecurityProbe.ForeignTenant, "403", status, status.Code == HttpStatusCode.Forbidden));
            }
        }

        return new SecurityProbeReport(results, loosened);
    }

    private static SecurityProbeResult Result(ProbeTarget target, SecurityProbe probe, string expected, ProbeStatus status, bool passed)
        => new(target.Method, target.Route, target.Url, probe, expected, (int)status.Code, passed);

    private static async Task<ProbeStatus> SendAsync(
        HttpClient client, ProbeTarget target, Caller? caller, SecurityProbeOptions options, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod(target.Method), target.Url);
        if (target.Method is not ("GET" or "HEAD" or "DELETE" or "OPTIONS"))
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        if (caller is not null)
        {
            // A fresh user per probe: no membership, so a selected company is foreign to it.
            request.Headers.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
            request.Headers.Add(TestAuthDefaults.UserNameHeader, "security-probe");
            if (caller.Roles.Length > 0)
                request.Headers.Add(TestAuthDefaults.RolesHeader, string.Join(',', caller.Roles));
            if (caller.Permissions.Length > 0)
                request.Headers.Add(TestAuthDefaults.PermissionsHeader, string.Join(',', caller.Permissions));
            if (caller.TenantId is { } tenant)
                request.Headers.Add(options.TenantHeader, tenant.ToString());
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.RequestTimeout);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        return new ProbeStatus(response.StatusCode, response.Headers.Location?.ToString());
    }

    private static List<ProbeTarget> Discover(IServiceProvider services, SecurityProbeOptions options)
    {
        var targets = new List<ProbeTarget>();
        var sources = services.GetServices<EndpointDataSource>();
        foreach (var endpoint in sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Distinct())
        {
            if (options.Skip?.Invoke(endpoint) == true)
                continue;

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } declared
                ? declared
                : ["GET"];
            var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var policies = authorize.Select(a => a.Policy).OfType<string>().Where(p => p.Length > 0).Distinct().ToArray();
            var roles = authorize
                .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct()
                .ToArray();
            var requiresMore = policies.Length > 0
                || roles.Length > 0
                || endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
                    .Any(p => p.Requirements.Any(r => r is not Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement));
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var route = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var url = BuildUrl(endpoint.RoutePattern, options);

            foreach (var method in methods)
                targets.Add(new ProbeTarget(method, route, url, anonymous, requiresMore, policies, roles));
        }

        return targets
            .DistinctBy(t => (t.Method, t.Route))
            .OrderBy(t => t.Route, StringComparer.Ordinal)
            .ThenBy(t => t.Method, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildUrl(RoutePattern pattern, SecurityProbeOptions options)
    {
        var segments = new List<string>();
        foreach (var segment in pattern.PathSegments)
        {
            var text = new StringBuilder();
            foreach (var part in segment.Parts)
            {
                text.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => Uri.EscapeDataString(options.RouteValue(parameter)),
                    _ => string.Empty,
                });
            }

            segments.Add(text.ToString());
        }

        return "/" + string.Join('/', segments);
    }

    private sealed record ProbeTarget(
        string Method, string Route, string Url, bool Anonymous, bool RequiresMoreThanSignIn, string[] Policies, string[] Roles);

    private readonly record struct ProbeStatus(HttpStatusCode Code, string? Location);

    private sealed record Caller(string[] Roles, string[] Permissions, Guid? TenantId);

    /// <summary>The default placeholder for a route parameter, chosen from its constraints.</summary>
    public static string DefaultRouteValue(RoutePatternParameterPart parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (parameter.Default is { } value)
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        var constraints = parameter.ParameterPolicies.Select(p => p.Content ?? string.Empty).ToList();
        if (constraints.Any(c => c is "int" or "long" or "decimal" or "double" or "float" || c.StartsWith("min(", StringComparison.Ordinal) || c.StartsWith("range(", StringComparison.Ordinal)))
            return "1";
        if (constraints.Contains("bool"))
            return "true";
        if (constraints.Contains("datetime"))
            return "2026-01-01";
        if (constraints.Any(c => c.StartsWith("alpha", StringComparison.Ordinal)))
            return "probe";
        return "00000000-0000-0000-0000-00000000c0de";
    }

    private static bool IsLoginRedirect(this SecurityProbeOptions options, ProbeStatus status)
        => options.LoginPathFragment is { Length: > 0 } fragment
            && status.Code is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.Found
            && status.Location?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Settings of <see cref="SecurityProbeSuite.RunAsync"/>.</summary>
public sealed class SecurityProbeOptions
{
    /// <summary>
    /// A company that exists in the app's tenant store and of which the probing users are not members; enables the
    /// foreign-tenant probe (needs <c>AddMultiTenancy(b =&gt; b.RequireMembership())</c>).
    /// </summary>
    public Guid? ForeignTenantId { get; set; }

    /// <summary>Endpoints to leave out (e.g. a hub that only speaks WebSockets).</summary>
    public Func<RouteEndpoint, bool>? Skip { get; set; }

    /// <summary>The placeholder for a route parameter; defaults to <see cref="SecurityProbeSuite.DefaultRouteValue"/>.</summary>
    public Func<RoutePatternParameterPart, string> RouteValue { get; set; } = SecurityProbeSuite.DefaultRouteValue;

    /// <summary>
    /// A redirect whose location contains this text counts as the anonymous challenge (cookie-authenticated pages
    /// redirect to their login page instead of answering <c>401</c>). Default <c>login</c>; null disables it.
    /// </summary>
    public string? LoginPathFragment { get; set; } = "login";

    /// <summary>
    /// Routes (as in the route pattern, with a leading <c>/</c>) whose anonymous answer is not <c>401</c> by design,
    /// with the status codes that count as refused. The default covers OpenIddict's userinfo endpoint, which the
    /// token server rejects itself (<c>400 invalid_request</c>) before ASP.NET Core authorization runs. Keep the
    /// list short and exact: accepting any 4xx would let an unpoliced endpoint that validates its body first pass.
    /// </summary>
    public IDictionary<string, int[]> AnonymousStatusOverrides { get; } = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["/connect/userinfo"] = [400, 401],
    };

    /// <summary>The header that selects a company (the multi-tenancy default <c>X-Tenant-Id</c>).</summary>
    public string TenantHeader { get; set; } = "X-Tenant-Id";

    /// <summary>Time allowed per request (default 15 seconds).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Which check a <see cref="SecurityProbeResult"/> is.</summary>
public enum SecurityProbe
{
    /// <summary>No credentials: expects <c>401</c> or a login redirect.</summary>
    Anonymous,

    /// <summary>Signed in without any permission or role: expects <c>403</c>.</summary>
    NoPermission,

    /// <summary>Holding the endpoint's permissions but selecting a company without a membership: expects <c>403</c>.</summary>
    ForeignTenant,
}

/// <summary>One probe of one endpoint.</summary>
public sealed record SecurityProbeResult(
    string Method, string Route, string Url, SecurityProbe Probe, string Expected, int Actual, bool Passed)
{
    /// <inheritdoc />
    public override string ToString() => $"{Probe}: {Method} {Url} expected {Expected}, got {Actual}";
}

/// <summary>The outcome of <see cref="SecurityProbeSuite.RunAsync"/>.</summary>
/// <param name="Results">Every probe run.</param>
/// <param name="Loosened">The anonymous endpoints, which were not probed (the startup guard reviews them).</param>
public sealed record SecurityProbeReport(IReadOnlyList<SecurityProbeResult> Results, IReadOnlyList<string> Loosened)
{
    /// <summary>The probes that did not get the expected answer.</summary>
    public IEnumerable<SecurityProbeResult> Failures => Results.Where(r => !r.Passed);

    /// <summary>Throws <see cref="SecurityProbeException"/> listing every failed probe.</summary>
    public void EnsureNoFailures()
    {
        var failures = Failures.ToList();
        if (failures.Count > 0)
            throw new SecurityProbeException(failures);
    }
}

/// <summary>Thrown by <see cref="SecurityProbeReport.EnsureNoFailures"/>.</summary>
public sealed class SecurityProbeException(IReadOnlyList<SecurityProbeResult> failures)
    : Exception($"{failures.Count} security probe(s) failed:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", failures))
{
    /// <summary>The failed probes.</summary>
    public IReadOnlyList<SecurityProbeResult> Failures { get; } = failures;
}
