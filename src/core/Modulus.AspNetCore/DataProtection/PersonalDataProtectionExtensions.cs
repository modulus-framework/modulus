namespace Modulus.AspNetCore.DataProtection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddDataProtection();

        // Swappable: a user registration made before this call wins.
        services.TryAddSingleton<IPersonalDataProtector, DataProtectionPersonalDataProtector>();
        return services;
    }
}
