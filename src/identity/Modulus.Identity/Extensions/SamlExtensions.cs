namespace Modulus.Identity.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;

/// <summary>SAML 2.0 sign-in (this server as the service provider).</summary>
public static class SamlExtensions
{
    /// <summary>Holds the relay state of a started sign-in in the browser that started it (see the login endpoint).</summary>
    private const string SamlBindingCookie = ".modulus.saml";

    /// <summary>
    /// Adds SAML 2.0 sign-in (settings <c>Identity:Saml</c>, <see cref="SamlOptions"/>): people sign in at one of several identity
    /// providers and arrive with the Identity cookie, so the authorization endpoint then issues them tokens as for a password
    /// sign-in. Accounts are created on first sign-in, without a password. Call after <c>AddModulusIdentity</c>; map the endpoints
    /// with <see cref="MapModulusSaml"/>. Off unless <c>Identity:Saml:Enabled</c> is true. Needs a migration for
    /// <c>ModulusSamlRequests</c>. <c>Identity:Saml:BaseUrl</c> falls back to <c>Identity:Issuer</c>.
    /// </summary>
    /// <typeparam name="TContext">The Identity context (derived from <c>ModulusIdentityDbContext</c>).</typeparam>
    /// <typeparam name="TUser">The user type.</typeparam>
    public static IServiceCollection AddModulusSaml<TContext, TUser>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
        where TUser : ModulusUser, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var issuer = configuration["Identity:Issuer"];
        services.AddOptions<SamlOptions>()
            .Bind(configuration.GetSection("Identity:Saml"))
            .PostConfigure(o => o.BaseUrl = string.IsNullOrWhiteSpace(o.BaseUrl) ? issuer ?? "" : o.BaseUrl)
            .Validate(o => o.Validate() is null, "Identity:Saml is not valid.")
            .ValidateOnStart();
        if (!configuration.GetValue<bool>("Identity:Saml:Enabled"))
            return services;

        // Say what is wrong instead of the generic message above.
        var bound = configuration.GetSection("Identity:Saml").Get<SamlOptions>() ?? new SamlOptions();
        if (string.IsNullOrWhiteSpace(bound.BaseUrl))
            bound.BaseUrl = issuer ?? "";
        if (bound.Validate() is { } problem)
            throw new InvalidOperationException(problem);

        services.TryAddScoped<ExternalAccountProvisioner<TUser>>();
        services.TryAddScoped<ISamlSignInService, SamlSignInService<TContext, TUser>>();
        return services;
    }

    /// <summary>
    /// Maps the SAML endpoints under <c>Identity:Saml:BasePath</c> (default <c>/saml</c>): <c>GET</c> (the providers, for the login
    /// page's buttons), <c>GET {provider}/login?returnUrl=</c> (sends the browser to the identity provider; the return address must
    /// be local), <c>POST {provider}/acs</c> (the identity provider's answer) and <c>GET {provider}/metadata</c>. Single logout is
    /// not supported. Does nothing when SAML is off.
    /// </summary>
    public static IEndpointRouteBuilder MapModulusSaml(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<SamlOptions>>().Value;
        if (!options.Enabled)
            return endpoints;

        var group = endpoints.MapGroup(options.BasePath);
        // These look up accounts, not company data: run in the host context, like the account endpoints.
        group.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.RequestServices.GetService<ICurrentTenant>() is not { } tenant)
                return await next(context);
            using (tenant.Change(null))
                return await next(context);
        });

        group.MapGet("/", (ISamlSignInService saml) => Results.Ok(saml.Providers.Select(p => new { name = p.Name, displayName = p.DisplayName })))
            .AllowAnonymous()
            .WithMetadata(new LoosenedAttribute("Lists the SAML providers so the login page can show their buttons: names only") { Framework = true });

        group.MapGet("/{provider}/login", async (string provider, string? returnUrl, HttpContext http, ISamlSignInService saml, CancellationToken ct) =>
        {
            var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            if (!SamlSignInService<DbContext, ModulusUser>.IsLocal(target))
                return Results.Problem(detail: "The return address must be a local path.", statusCode: StatusCodes.Status400BadRequest);
            if (await saml.BeginAsync(provider, target, ct) is not { } location)
                return Results.NotFound();

            // Bind the request to this browser: the answer is accepted only with the cookie this response sets (login CSRF).
            var relay = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query)["RelayState"].ToString();
            http.Response.Cookies.Append(SamlBindingCookie, relay, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = options.BasePath,
                MaxAge = TimeSpan.FromSeconds(options.RequestLifetimeSeconds),
            });
            return Results.Redirect(location.AbsoluteUri);
        })
            .AllowAnonymous()
            .WithMetadata(new LoosenedAttribute("Starts a SAML sign-in: nobody is signed in yet; the return address is checked to be local") { Framework = true });

        group.MapPost("/{provider}/acs", async (string provider, HttpContext http, ISamlSignInService saml, CancellationToken ct) =>
        {
            // Read by hand: this is a cross-site form post from the identity provider, so it carries no antiforgery token. It is
            // protected by the relay state (a one-shot value only this server issued) and by the response's signature.
            if (!http.Request.HasFormContentType)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            var form = await http.Request.ReadFormAsync(ct);
            // The browser that started the sign-in holds the binding cookie; an answer without it (or for another request) is refused
            // before the signature is even checked, and the cookie is single-use.
            var binding = http.Request.Cookies[SamlBindingCookie];
            var relay = form["RelayState"].ToString();
            http.Response.Cookies.Delete(SamlBindingCookie, new CookieOptions { Path = options.BasePath, Secure = true, SameSite = SameSiteMode.None });
            if (string.IsNullOrEmpty(binding) || string.IsNullOrEmpty(relay)
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(binding), System.Text.Encoding.UTF8.GetBytes(relay)))
                return Results.Problem(title: "Sign-in was refused.", detail: "The sign-in could not be completed. Try again, or ask an administrator.", statusCode: StatusCodes.Status403Forbidden);

            var result = await saml.CompleteAsync(provider, form["SAMLResponse"], form["RelayState"], ct);
            return result.Succeeded
                ? Results.Redirect(result.ReturnUrl!)
                : Results.Problem(title: "Sign-in was refused.", detail: "The sign-in could not be completed. Try again, or ask an administrator.", statusCode: StatusCodes.Status403Forbidden);
        })
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithMetadata(new LoosenedAttribute("SAML assertion consumer service: the identity provider posts the signed response here; it is authenticated by its signature and a one-shot relay state") { Framework = true });

        group.MapGet("/{provider}/metadata", (string provider, ISamlSignInService saml)
            => saml.Metadata(provider) is { } xml ? Results.Content(xml, "application/samlmetadata+xml") : Results.NotFound())
            .AllowAnonymous()
            .WithMetadata(new LoosenedAttribute("SAML service provider metadata: public by design, holds only the entity id, callback address and public certificate") { Framework = true });

        return endpoints;
    }
}
