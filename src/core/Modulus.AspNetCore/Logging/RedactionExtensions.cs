namespace Modulus.AspNetCore.Logging;

using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Compliance;

/// <summary>Settings of <see cref="RedactionExtensions.AddModulusRedaction"/> (section <c>Security:Redaction</c>).</summary>
public sealed class ModulusRedactionOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Security:Redaction";

    /// <summary>
    /// Redact classified values (default <see langword="true"/>). <see langword="false"/> (a developer's machine) writes them
    /// as they are, except <see cref="ModulusTaxonomy.Secret"/>, which is always erased.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base64 key (at least 32 bytes) for pseudonymizing <see cref="ModulusTaxonomy.Personal"/> values with HMAC-SHA256: the same
    /// value always gives the same token, so log lines about one person still correlate. Without a key personal values are
    /// erased. Supply it from user secrets, the environment or a vault, never a committed file (the secrets guard refuses it).
    /// </summary>
    public string? HmacKey { get; set; }

    /// <summary>Identifies <see cref="HmacKey"/> in the output (<c>{KeyId}:{hash}</c>) so a rotated key is recognisable.</summary>
    public int? HmacKeyId { get; set; }
}

/// <summary>Log redaction for the <see cref="ModulusTaxonomy"/> classifications.</summary>
public static class RedactionExtensions
{
    /// <summary>The smallest accepted <see cref="ModulusRedactionOptions.HmacKey"/>, in bytes.</summary>
    public const int MinimumHmacKeyBytes = 32;

    /// <summary>
    /// Redacts classified values in logs written through source-generated logging (<c>[LoggerMessage]</c> parameters and
    /// <c>[LogProperties]</c> members carrying a classification attribute):
    /// <list type="bullet">
    /// <item><see cref="ModulusTaxonomy.Internal"/>: written as is;</item>
    /// <item><see cref="ModulusTaxonomy.Personal"/> (and <c>[ProtectedPersonalData]</c>): HMAC-SHA256 with
    /// <see cref="ModulusRedactionOptions.HmacKey"/>, else erased;</item>
    /// <item><see cref="ModulusTaxonomy.Confidential"/>, <see cref="ModulusTaxonomy.Restricted"/>,
    /// <see cref="ModulusTaxonomy.Secret"/> and any other classification: erased.</item>
    /// </list>
    /// Ordinary <c>logger.LogInformation("{Email}", email)</c> calls are not redacted; analyzer MOD0002 reports a classified
    /// value passed to one. A project that declares classified <c>[LoggerMessage]</c> methods must reference
    /// <c>Microsoft.Extensions.Telemetry.Abstractions</c> directly (its generator honours the attributes; the built-in one
    /// ignores them).
    /// </summary>
    /// <exception cref="InvalidOperationException">The HMAC key is not base64 or is shorter than 32 bytes.</exception>
    public static IServiceCollection AddModulusRedaction(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusRedactionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Read now: the redactor map is fixed at registration.
        var options = new ModulusRedactionOptions();
        configuration.GetSection(ModulusRedactionOptions.SectionName).Bind(options);
        configure?.Invoke(options);
        var hmacKey = ValidateHmacKey(options.HmacKey);

        services.AddRedaction(redaction =>
        {
            redaction.SetRedactor<ErasingRedactor>(new DataClassificationSet(ModulusTaxonomy.Secret));
            if (!options.Enabled)
            {
                redaction.SetFallbackRedactor<NullRedactor>();
                return;
            }

            redaction.SetRedactor<NullRedactor>(new DataClassificationSet(ModulusTaxonomy.Internal));
            redaction.SetRedactor<ErasingRedactor>(
                new DataClassificationSet(ModulusTaxonomy.Confidential),
                new DataClassificationSet(ModulusTaxonomy.Restricted));
            if (hmacKey is null)
            {
                redaction.SetRedactor<ErasingRedactor>(new DataClassificationSet(ModulusTaxonomy.Personal));
            }
            else
            {
                redaction.SetHmacRedactor(
                    h =>
                    {
                        h.Key = hmacKey;
                        h.KeyId = options.HmacKeyId;
                    },
                    new DataClassificationSet(ModulusTaxonomy.Personal));
            }

            redaction.SetFallbackRedactor<ErasingRedactor>();
        });

        services.AddLogging(logging => logging.EnableRedaction());
        return services;
    }

    private static string? ValidateHmacKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{ModulusRedactionOptions.SectionName}:HmacKey must be base64.");
        }

        return bytes.Length >= MinimumHmacKeyBytes
            ? key
            : throw new InvalidOperationException(
                $"{ModulusRedactionOptions.SectionName}:HmacKey must be at least {MinimumHmacKeyBytes} bytes (base64).");
    }
}
