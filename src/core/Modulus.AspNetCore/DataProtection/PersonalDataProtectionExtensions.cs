namespace Modulus.AspNetCore.DataProtection;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// Registers transparent at-rest encryption for personal data. Marked properties
/// (<see cref="ProtectedPersonalDataAttribute"/>) are encrypted by each module's
/// DbContext via an <see cref="IPersonalDataProtector"/> backed by ASP.NET Core Data
/// Protection. Configuration lives under the <c>PersonalDataProtection</c> section
/// (see <see cref="PersonalDataProtectionOptions"/>).
/// </summary>
public static class PersonalDataProtectionExtensions
{
    /// <summary>
    /// Binds <see cref="PersonalDataProtectionOptions"/>, ensures Data Protection is
    /// available, and registers the default <see cref="IPersonalDataProtector"/>. When
    /// <see cref="PersonalDataProtectionOptions.Enabled"/> is <c>false</c> nothing is
    /// registered and marked columns stay plaintext. Register your own
    /// <see cref="IPersonalDataProtector"/> before calling this to override the default.
    /// </summary>
    public static IServiceCollection AddModulusPersonalDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PersonalDataProtectionOptions>? configure = null)
    {
        // Resolve the effective Enabled flag up front so encryption can be switched off
        // entirely (no protector registered → the DbContext hook is a no-op).
        var options = new PersonalDataProtectionOptions();
        configuration.GetSection(PersonalDataProtectionOptions.SectionName).Bind(options);
        configure?.Invoke(options);
        if (!options.Enabled)
            return services;

        services.AddOptions<PersonalDataProtectionOptions>()
            .Bind(configuration.GetSection(PersonalDataProtectionOptions.SectionName));
        if (configure is not null)
            services.Configure(configure);

        // Data Protection provides the key ring (storage, rotation, ring management).
        // Idempotent, so it composes with any other consumer of Data Protection.
        var dp = services.AddDataProtection();
        dp.SetApplicationName(options.ApplicationName);
        if (!string.IsNullOrWhiteSpace(options.KeyRingDirectory))
            dp.PersistKeysToFileSystem(new DirectoryInfo(options.KeyRingDirectory!));

        // Checked at startup, not here: a ring persisted in code (PersistKeysToDbContext,
        // PersistKeysToStackExchangeRedis, ...) is only visible once options are built.
        services.AddHostedService<KeyRingPersistenceGuard>();

        // Swappable: a user registration made before this call wins.
        services.TryAddSingleton<IPersonalDataProtector, DataProtectionPersonalDataProtector>();
        return services;
    }

    /// <summary>
    /// Fails startup in Production when the Data Protection key ring is not persisted (no
    /// <see cref="KeyManagementOptions.XmlRepository"/>, from <see cref="PersonalDataProtectionOptions.KeyRingDirectory"/>
    /// or a <c>PersistKeysTo*</c> call); elsewhere it warns. An unpersisted ring is lost on restart and differs
    /// per replica, so encrypted columns become undecryptable.
    /// </summary>
    private sealed class KeyRingPersistenceGuard(
        IOptions<KeyManagementOptions> keyManagement,
        IHostEnvironment environment,
        ILogger<KeyRingPersistenceGuard> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            if (keyManagement.Value.XmlRepository is not null)
                return Task.CompletedTask;

            if (environment.IsProduction())
                throw new InvalidOperationException(
                    "PersonalDataProtection is enabled in Production but the Data Protection key ring is not persisted. " +
                    "Set PersonalDataProtection:KeyRingDirectory or call a PersistKeysTo* method, or encrypted data becomes undecryptable on restart/scale-out.");

            logger.LogWarning(
                "PersonalDataProtection uses an unpersisted Data Protection key ring. " +
                "Persist the ring before Production or encrypted columns become undecryptable on restart/scale-out.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
