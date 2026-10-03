using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Secure by default: <c>AddModulusAuthorization</c> closes endpoints that declare no policy.</summary>
[Trait("Category", "Unit")]
public sealed class AuthorizationFallbackPolicyTests
{
    private static AuthorizationOptions Options(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IOptions<AuthorizationOptions>>().Value;
    }

    [Fact]
    public void The_fallback_policy_requires_a_signed_in_user()
    {
        var options = Options(s => s.AddModulusAuthorization());

        options.FallbackPolicy.Should().NotBeNull();
        options.FallbackPolicy!.Requirements.Should().ContainSingle()
            .Which.Should().BeOfType<Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement>();
    }

    [Fact]
    public void The_default_can_be_turned_off()
        => Options(s => s.AddModulusAuthorization(o => o.RequireAuthenticatedUserByDefault = false))
            .FallbackPolicy.Should().BeNull();

    [Fact]
    public void A_fallback_policy_the_app_set_is_kept()
    {
        var own = new AuthorizationPolicyBuilder().RequireRole("Staff").Build();

        var options = Options(s =>
        {
            s.AddAuthorization(o => o.FallbackPolicy = own);
            s.AddModulusAuthorization();
            s.AddModulusAuthorization();
        });

        options.FallbackPolicy.Should().BeSameAs(own);
    }
}
