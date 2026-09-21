using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.UI.Theming.Tabler;

namespace Modulus.UI.Ejection.Tests;

/// <summary>
/// A TestServer host that behaves like an app that ejected the framework's views: this assembly (the "app") comes first
/// in the application parts, then the packages the views belong to, as in a real host where the entry assembly leads.
/// </summary>
internal sealed class EjectionHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private EjectionHost(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<EjectionHost> StartAsync(
        Action<IServiceCollection>? services = null,
        params System.Reflection.Assembly[] packages)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>());

        services?.Invoke(builder.Services);

        // This host tests view resolution (which assembly serves a page's
        // markup), not authorization — but feature-page models carry a bare
        // [Authorize] floor, so every request needs a signed-in principal or
        // AddRazorPages()'s authorization services (picked up by
        // WebApplication's implicit UseAuthorization insertion) reject it.
        // Always authenticate as a fixed test user unless the test supplied
        // its own scheme.
        if (!builder.Services.Any(d => d.ServiceType == typeof(IAuthenticationSchemeProvider)))
        {
            builder.Services.AddAuthentication(AlwaysAuthenticatedHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticatedHandler>(
                    AlwaysAuthenticatedHandler.SchemeName, _ => { });
        }

        builder.Services.AddTablerTheme(builder.Configuration);

        var mvc = builder.Services.AddRazorPages().AddApplicationPart(typeof(EjectionHost).Assembly);
        foreach (var package in packages.Prepend(typeof(TablerTheme).Assembly).Prepend(typeof(UiOptions).Assembly))
        {
            mvc.AddApplicationPart(package);
        }

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        await app.StartAsync();
        return new EjectionHost(app, app.GetTestClient());
    }

    /// <summary>The assembly the view engine serves <paramref name="path"/> from, or null when no assembly has it.</summary>
    public async Task<System.Reflection.Assembly?> ServedFromAsync(string path)
    {
        var compiler = Services.GetRequiredService<IViewCompilerProvider>().GetCompiler();
        var descriptor = await compiler.CompileAsync(path);
        return descriptor.Type?.Assembly;
    }

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

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Fallback authentication scheme: every request is a signed-in "test-user" unless the test registers its own scheme.</summary>
    private sealed class AlwaysAuthenticatedHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "EjectionHostFallback";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
