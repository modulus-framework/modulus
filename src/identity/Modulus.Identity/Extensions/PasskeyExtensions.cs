namespace Modulus.Identity.Extensions;

using System.Security.Claims;
using System.Text.Json;
using Fido2NetLib;
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

/// <summary>Passkey (WebAuthn) sign-in.</summary>
public static class PasskeyExtensions
{
    /// <summary>
    /// Adds passkeys (settings <c>Identity:Passkeys</c>, <see cref="PasskeyOptions"/>): <see cref="IPasskeyService"/>, the
    /// <see cref="PasskeyGrant"/> at <c>/connect/token</c> and, with <see cref="MapModulusPasskeys"/>, the ceremony endpoints.
    /// Call after <c>AddModulusIdentity</c>/<c>AddModulusOpenIddict</c>. Off unless <c>Identity:Passkeys:Enabled</c> is true. A
    /// passkey sign-in token carries <c>amr</c> <c>mfa</c>, so it satisfies step-up authentication. Needs a migration for
    /// <c>ModulusPasskeys</c> and <c>ModulusPasskeyChallenges</c>.
    /// </summary>
    /// <typeparam name="TContext">The Identity context (derived from <c>ModulusIdentityDbContext</c>).</typeparam>
    /// <typeparam name="TUser">The user type.</typeparam>
    public static IServiceCollection AddModulusPasskeys<TContext, TUser>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
        where TUser : ModulusUser, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PasskeyOptions>()
            .Bind(configuration.GetSection("Identity:Passkeys"))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PasskeyOptions>, PasskeyOptionsValidator>());

        if (!configuration.GetValue<bool>("Identity:Passkeys:Enabled"))
            return services;

        services.AddOpenIddict().AddServer(server => server.AllowCustomFlow(PasskeyGrant.GrantType));
        services.TryAddSingleton<IFido2>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PasskeyOptions>>().Value;
            return new Fido2(new Fido2Configuration
            {
                RPID = settings.RelyingPartyId,
                RPName = settings.RelyingPartyName ?? settings.RelyingPartyId,
                Origins = new HashSet<string>(settings.Origins, StringComparer.Ordinal),
                Timeout = (uint)settings.ChallengeLifetimeSeconds * 1000,
            }, null!);
        });
        services.TryAddScoped<IPasskeyService, PasskeyService<TContext, TUser>>();
        return services;
    }

    /// <summary>
    /// Maps the ceremony endpoints under <paramref name="prefix"/> (default <c>/account/passkeys</c>): <c>POST login/options</c>
    /// (anonymous; the answer goes to <c>/connect/token</c> with <see cref="PasskeyGrant"/>), and for the signed-in person
    /// <c>POST register/options</c>, <c>POST register/complete</c>, <c>GET</c> and <c>DELETE {id}</c>. Adding or removing a
    /// passkey needs a recent sign-in (<see cref="PasskeyOptions.ManageMaxAgeMinutes"/>). The default rate limiter's sensitive
    /// paths already cover <c>/account</c>.
    /// </summary>
    public static RouteGroupBuilder MapModulusPasskeys(this IEndpointRouteBuilder endpoints, string prefix = "/account/passkeys")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix);
        // Like the account controller, these look up accounts, not company data: run in the host context.
        group.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.RequestServices.GetService<ICurrentTenant>() is not { } tenant)
                return await next(context);
            using (tenant.Change(null))
                return await next(context);
        });

        group.MapPost("/login/options", async (IPasskeyService passkeys, CancellationToken ct) => Results.Ok(await passkeys.BeginLoginAsync(ct)))
            .AllowAnonymous()
            .WithMetadata(new LoosenedAttribute("Starts a passkey sign-in: nobody is signed in yet, and the options hold only a random challenge") { Framework = true });

        var signedIn = group.MapGroup("").RequireAuthorization();

        signedIn.MapPost("/register/options", async (ClaimsPrincipal caller, IPasskeyService passkeys, IOptions<PasskeyOptions> options, TimeProvider clock, CancellationToken ct) =>
        {
            if (UserOf(caller) is not { } userId)
                return Results.Unauthorized();
            if (!SignedInRecently(caller, options.Value, clock))
                return RecentSignInRequired(options.Value);
            try
            {
                return await passkeys.BeginRegistrationAsync(userId, ct) is { } ceremony ? Results.Ok(ceremony) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        signedIn.MapPost("/register/complete", async (RegisterPasskeyRequest body, ClaimsPrincipal caller, IPasskeyService passkeys, IOptions<PasskeyOptions> options, TimeProvider clock, CancellationToken ct) =>
        {
            if (UserOf(caller) is not { } userId)
                return Results.Unauthorized();
            if (!SignedInRecently(caller, options.Value, clock))
                return RecentSignInRequired(options.Value);
            if (body.Credential.ValueKind != JsonValueKind.Object)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["credential"] = ["The browser's credential is required."] });

            return await passkeys.CompleteRegistrationAsync(userId, body.ChallengeId, body.Name ?? "", body.Credential.GetRawText(), ct) is { } created
                ? Results.Ok(created)
                : Results.Problem(detail: "The passkey could not be verified.", statusCode: StatusCodes.Status400BadRequest);
        });

        signedIn.MapGet("/", async (ClaimsPrincipal caller, IPasskeyService passkeys, CancellationToken ct) =>
            UserOf(caller) is { } userId ? Results.Ok(await passkeys.ListAsync(userId, ct)) : Results.Unauthorized());

        signedIn.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal caller, IPasskeyService passkeys, IOptions<PasskeyOptions> options, TimeProvider clock, CancellationToken ct) =>
        {
            if (UserOf(caller) is not { } userId)
                return Results.Unauthorized();
            if (!SignedInRecently(caller, options.Value, clock))
                return RecentSignInRequired(options.Value);
            return await passkeys.RemoveAsync(userId, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }

    private static Guid? UserOf(ClaimsPrincipal caller)
        => Guid.TryParse(caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub"), out var id) ? id : null;

    private static bool SignedInRecently(ClaimsPrincipal caller, PasskeyOptions options, TimeProvider clock)
    {
        if (!long.TryParse(caller.FindFirstValue("auth_time"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return false;
        var age = clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        return age >= TimeSpan.FromMinutes(-1) && age <= TimeSpan.FromMinutes(options.ManageMaxAgeMinutes);
    }

    private static IResult RecentSignInRequired(PasskeyOptions options)
        => Results.Problem(
            title: "Recent sign-in required",
            detail: $"Sign in again (within {options.ManageMaxAgeMinutes} minutes) to change passkeys.",
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = "RECENT_SIGN_IN_REQUIRED" });

    private sealed record RegisterPasskeyRequest(Guid ChallengeId, string? Name, JsonElement Credential);

    private sealed class PasskeyOptionsValidator : IValidateOptions<PasskeyOptions>
    {
        public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
            => options.IsValid(out var problem)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(problem ?? "Identity:Passkeys is misconfigured.");
    }
}
