using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.Extensions;

/// <summary>Registration of directory (LDAP / Active Directory) sign-in.</summary>
public static class LdapExtensions
{
    /// <summary>
    /// Lets the password grant sign people in against LDAP / Active Directory (<c>Identity:Ldap</c>). Call after
    /// <c>AddModulusIdentity</c>: it wraps the validator registered there. Does nothing when <c>Identity:Ldap:Enabled</c> is false.
    /// Accounts are created on first sign-in (no local password) and linked to the directory entry by its stable id.
    /// </summary>
    /// <typeparam name="TUser">The application's user type, as given to <c>AddModulusIdentity</c>.</typeparam>
    public static IServiceCollection AddModulusLdap<TUser>(this IServiceCollection services, IConfiguration configuration)
        where TUser : ModulusUser, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LdapOptions>()
            .Bind(configuration.GetSection("Identity:Ldap"))
            .Validate(o => o.IsValid(out _), "Identity:Ldap is not valid (a directory needs Host, BaseDn, a {0} filter and TLS).")
            .ValidateOnStart();
        if (!configuration.GetValue<bool>("Identity:Ldap:Enabled"))
            return services;

        // Say what is wrong instead of the generic message above.
        var bound = configuration.GetSection("Identity:Ldap").Get<LdapOptions>() ?? new LdapOptions();
        if (!bound.IsValid(out var problem))
            throw new InvalidOperationException(problem);

        services.TryAddSingleton<ILdapDirectoryClient, SystemLdapDirectoryClient>();
        services.TryAddScoped<ExternalAccountProvisioner<TUser>>();

        // Wrap whatever validator is registered (the Identity one, or an app's own) so local passwords keep working as a fallback.
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IPasswordGrantCredentialValidator))
            ?? throw new InvalidOperationException("AddModulusLdap must be called after AddModulusIdentity.");
        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(typeof(LocalPasswordGrantValidator), sp => new LocalPasswordGrantValidator(Build(sp, existing)), existing.Lifetime));
        services.Add(ServiceDescriptor.Describe(
            typeof(IPasswordGrantCredentialValidator),
            sp => ActivatorUtilities.CreateInstance<LdapPasswordGrantValidator<TUser>>(sp, sp.GetRequiredService<LocalPasswordGrantValidator>().Inner),
            existing.Lifetime));
        return services;
    }

    private static IPasswordGrantCredentialValidator Build(IServiceProvider sp, ServiceDescriptor descriptor)
        => descriptor switch
        {
            { ImplementationInstance: IPasswordGrantCredentialValidator instance } => instance,
            { ImplementationFactory: { } factory } => (IPasswordGrantCredentialValidator)factory(sp),
            { ImplementationType: { } type } => (IPasswordGrantCredentialValidator)ActivatorUtilities.CreateInstance(sp, type),
            _ => throw new InvalidOperationException("The password validator registration cannot be wrapped."),
        };

    /// <summary>Holds the wrapped validator so DI can resolve it once per scope.</summary>
    internal sealed class LocalPasswordGrantValidator(IPasswordGrantCredentialValidator inner)
    {
        public IPasswordGrantCredentialValidator Inner { get; } = inner;
    }
}
