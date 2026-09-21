using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// <c>AddModulusOpenIddict</c> installs a deny-everything password validator and <c>AddModulusIdentity</c> replaces it.
/// The outcome must not depend on which is called first: a module's <c>ConfigureServices</c> (which runs inside
/// <c>AddModulus</c>) often registers Identity before Program.cs reaches <c>AddModulusOpenIddict</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasswordValidatorRegistrationTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private static Type? EffectiveValidator(IServiceCollection services)
        => services.Last(d => d.ServiceType == typeof(IPasswordGrantCredentialValidator)).ImplementationType;

    private static void AddIdentity(IServiceCollection services)
        => services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(Configuration);

    [Fact]
    public void Identity_registered_after_openiddict_verifies_credentials()
    {
        var services = new ServiceCollection();
        services.AddModulusOpenIddict(Configuration);
        AddIdentity(services);

        EffectiveValidator(services)!.Name.Should().StartWith("IdentityPasswordGrantValidator");
    }

    [Fact]
    public void Identity_registered_before_openiddict_is_not_clobbered_by_the_deny_default()
    {
        var services = new ServiceCollection();
        AddIdentity(services);
        services.AddModulusOpenIddict(Configuration);

        EffectiveValidator(services)!.Name.Should().StartWith("IdentityPasswordGrantValidator");
    }

    [Fact]
    public void Openiddict_alone_still_denies_every_password_grant()
    {
        var services = new ServiceCollection();
        services.AddModulusOpenIddict(Configuration);

        EffectiveValidator(services).Should().Be(typeof(NullPasswordGrantCredentialValidator));
    }
}
