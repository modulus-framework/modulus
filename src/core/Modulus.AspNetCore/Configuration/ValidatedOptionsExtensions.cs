namespace Modulus.AspNetCore.Configuration;

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers strongly-typed options that are validated with DataAnnotations
/// and <c>ValidateOnStart</c>, so a misconfigured deployment fails fast at boot
/// instead of throwing on the first request that touches the bad value.
/// </summary>
public static class ValidatedOptionsExtensions
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/> from <paramref name="configuration"/>
    /// section <paramref name="sectionName"/>, enforcing <see cref="ValidationAttribute"/>s
    /// at application start.
    /// </summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        Action<TOptions>? configure = null)
        where TOptions : class
    {
        var builder = services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
            builder.Configure(configure);

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TOptions"/> with a custom predicate that
    /// must return <see langword="true"/> for the configuration to be considered
    /// valid, enforced at application start.
    /// </summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        Func<TOptions, bool> validate,
        string failureMessage)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .Validate(validate, failureMessage)
            .ValidateOnStart();

        return services;
    }
}
