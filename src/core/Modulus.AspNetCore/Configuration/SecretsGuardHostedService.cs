namespace Modulus.AspNetCore.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Runs the <see cref="SecretsGuardScanner"/> once at startup. In the configured
/// environments it fails fast (or warns) when a sensitive value is sourced from a
/// committed <c>appsettings*.json</c> file, so the mistake surfaces before the app
/// starts serving rather than as a leaked secret in source control.
/// </summary>
internal sealed class SecretsGuardHostedService(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<SecretsGuardOptions> options,
    ILogger<SecretsGuardHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
            return Task.CompletedTask;

        if (!settings.Environments.Contains(environment.EnvironmentName, StringComparer.OrdinalIgnoreCase))
            return Task.CompletedTask;

        // The guard needs the full provider list; the DI-registered IConfiguration is
        // the root, but guard defensively in case a host swaps it for a plain section.
        if (configuration is not IConfigurationRoot root)
            return Task.CompletedTask;

        var violations = SecretsGuardScanner.Scan(root, environment.ContentRootPath, settings);
        if (violations.Count == 0)
            return Task.CompletedTask;

        var message = BuildMessage(violations);
        if (settings.FailOnViolation)
            throw new InvalidOperationException(message);

        logger.LogWarning("{SecretsGuardMessage}", message);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string BuildMessage(IReadOnlyList<SecretViolation> violations)
    {
        var lines = violations.Select(v => $"  - '{v.Key}' (from {v.Source})");
        return
            "Secrets guard: sensitive configuration values are sourced from committed JSON " +
            "files instead of environment variables, User Secrets, or a vault:\n" +
            string.Join("\n", lines) +
            "\nMove them out of appsettings*.json — in Development run " +
            "'dotnet user-secrets set <Key> <value>', in Production use environment variables " +
            "or a secrets manager. Set SecretsGuard:FailOnViolation to false to downgrade this " +
            "to a warning, or SecretsGuard:Enabled to false to disable the guard.";
    }
}
