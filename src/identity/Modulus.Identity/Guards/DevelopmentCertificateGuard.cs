namespace Modulus.Identity.Guards;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;

/// <summary>
/// Fails fast at startup if <see cref="ModulusIdentityOptions.UseDevelopmentCertificates"/>
/// is enabled in a Production environment.
/// </summary>
/// <remarks>
/// <para>
/// Nothing previously checked this at runtime — only the option's own doc
/// comment and this class's registration point asserted "Development only".
/// A config file that carries <c>Identity:UseDevelopmentCertificates=true</c>
/// into production (the common way this happens: a shared base
/// <c>appsettings.json</c> with no override in <c>appsettings.Production.json</c>)
/// would silently sign every token with OpenIddict's ephemeral,
/// regenerated-per-restart development certificate — the exact outcome the
/// setting's own documentation says must never happen in production.
/// </para>
/// <para>
/// The guard is auto-registered by <c>AddModulusOpenIddict</c>.
/// </para>
/// </remarks>
internal sealed class DevelopmentCertificateGuard(
    IHostEnvironment environment,
    IOptions<ModulusIdentityOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.UseDevelopmentCertificates && environment.IsProduction())
            throw new InvalidOperationException(
                "Identity:UseDevelopmentCertificates is true in a Production " +
                "environment. Development signing/encryption certificates are " +
                "ephemeral (regenerated on every restart) and must never sign " +
                "production tokens. Remove this setting from the Production " +
                "configuration (or override it to false there) and register " +
                "real certificates via the AddModulusOpenIddict configure " +
                "callback, e.g. options.AddSigningCertificate(...).");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
