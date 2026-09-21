using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.UI;
using Modulus.UI.Theming.Tabler;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// In-memory host (TestServer) that runs the real Razor pipeline against the
/// Tabler theme. Routes:
/// <list type="bullet">
/// <item><c>/probe/{layout}</c> (and <c>/catalog/products</c>) render a probe view inside the named layout;
/// append <c>?as=alice</c> to sign in as that user.</item>
/// <item><c>/_error/{code}</c> — the framework error pages.</item>
/// </list>
/// </summary>
internal sealed class ThemeHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ThemeHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>Starts a TestServer host running the Tabler theme.</summary>
    /// <param name="config">In-memory configuration (e.g. <c>Modulus:Ui:Branding:AppName</c>).</param>
    /// <param name="services">
    /// Registers test doubles <em>before</em> the theme, so <c>TryAdd</c> defaults
    /// (e.g. the fail-closed <c>ICurrentUser</c>) yield to them.
    /// </param>
    /// <param name="applicationParts">Extra assemblies whose Razor Pages / views to host (feature UIs).</param>
    /// <param name="pipeline">Adds middleware (authentication, authorization) after the <c>?as=</c> sign-in and before the pages are mapped.</param>
    public static async Task<ThemeHost> StartAsync(
        IDictionary<string, string?>? config = null,
        Action<IServiceCollection>? services = null,
        IEnumerable<System.Reflection.Assembly>? applicationParts = null,
        Action<WebApplication>? pipeline = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();

        if (config is not null)
        {
            builder.Configuration.AddInMemoryCollection(config);
        }

        services?.Invoke(builder.Services);

        // Feature-page models carry a bare [Authorize] floor (Modulus.UI.*'s
        // fail-closed-by-default fix), and AddRazorPages() registers
        // authorization services that WebApplication's implicit middleware
        // then auto-inserts UseAuthorization() for — even though this host
        // never calls it explicitly. Without any authentication scheme, an
        // anonymous request to a protected page throws (no default challenge
        // scheme) instead of cleanly 401ing. Tests that care about auth
        // register their own scheme via `services:` (see
        // PageAuthorizationTests); everyone else gets this minimal fallback
        // so "?as=name" (set below) keeps authenticating as before and a
        // truly anonymous request gets a plain 401.
        if (!builder.Services.Any(d => d.ServiceType == typeof(IAuthenticationSchemeProvider)))
        {
            builder.Services.AddAuthentication(AnonymousDenyHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, AnonymousDenyHandler>(
                    AnonymousDenyHandler.SchemeName, _ => { });
        }

        builder.Services.AddTablerTheme(builder.Configuration);
        var mvc = builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(UiOptions).Assembly)
            .AddApplicationPart(typeof(TablerTheme).Assembly)
            .AddApplicationPart(typeof(ThemeHost).Assembly);
        foreach (var part in applicationParts ?? [])
        {
            mvc.AddApplicationPart(part);
        }

        var app = builder.Build();

        app.Use((http, next) =>
        {
            if (http.Request.Query.TryGetValue("as", out var name) && !string.IsNullOrEmpty(name))
            {
                http.User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, name.ToString())], "test"));
            }

            return next(http);
        });

        // Explicit, not relying on WebApplication's implicit middleware
        // insertion: that inserts UseAuthentication/UseAuthorization near the
        // front of the pipeline (registered here or not), which would run
        // them BEFORE the "?as=" middleware above ever sets HttpContext.User
        // — authorizing every request as anonymous regardless of "?as=".
        // Calling them here, after "?as=", both fixes the ordering and
        // suppresses the implicit insertion (it only fires when neither was
        // called explicitly).
        app.UseAuthentication();
        app.UseAuthorization();

        pipeline?.Invoke(app);

        app.MapRazorPages();
        app.MapModulusErrorPages();
        app.MapGet("/probe/{layout}", (string layout) => new ProbeView($"/Views/Probe/{layout}.cshtml"));
        app.MapGet("/catalog/products", () => new ProbeView("/Views/Probe/Application.cshtml"));

        await app.StartAsync();
        return new ThemeHost(app, app.GetTestClient());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>GETs a page, surfacing the response body (the developer exception page) when it fails.</summary>
    public async Task<string> GetStringAsync(string url)
    {
        using var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GET {url} -> {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    /// <summary>Executes a compiled Razor view by absolute path (minimal APIs have no view result).</summary>
    private sealed class ProbeView(string viewPath) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var actionContext = new ActionContext(
                httpContext,
                httpContext.GetRouteData(),
                new ActionDescriptor());
            var viewData = new ViewDataDictionary(
                httpContext.RequestServices.GetRequiredService<IModelMetadataProvider>(),
                new ModelStateDictionary());

            return new ViewResult { ViewName = viewPath, ViewData = viewData }
                .ExecuteResultAsync(actionContext);
        }
    }

    /// <summary>
    /// Fallback authentication scheme registered when a test supplies none of
    /// its own: never authenticates a request on its own (the "?as=" middleware
    /// above sets <see cref="HttpContext.User"/> directly for tests that want a
    /// signed-in caller), and answers a bare 401 on challenge instead of
    /// redirecting or throwing — matching how a real host's default scheme
    /// behaves for an API-shaped anonymous request.
    /// </summary>
    private sealed class AnonymousDenyHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ThemeHostFallback";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
    }
}
