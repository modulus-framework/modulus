using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Several Modulus packages register the fail-closed <see cref="NullCurrentUser"/> with <c>TryAdd</c> as a default. When one ran before
/// <c>AddModulusIdentity</c> the identity adapter was never registered, so a signed-in user was anonymous to every <see cref="ICurrentUser"/>
/// consumer: a generated web app's menu, permission-gated for the administrator, came out empty.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CurrentUserRegistrationTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private static void AddIdentity(IServiceCollection services)
        => services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(Configuration);

    private static Type? Effective(IServiceCollection services)
        => services.Last(d => d.ServiceType == typeof(ICurrentUser)).ImplementationType;

    private sealed class AppCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? UserName => "app";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => true;
        public bool HasPermission(string permission) => true;
        public IReadOnlyList<string> Permissions => [];
    }

    [Fact]
    public void Identity_registered_after_the_fail_closed_default_replaces_it()
    {
        var services = new ServiceCollection();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        AddIdentity(services);

        Effective(services).Should().Be(typeof(ClaimsPrincipalCurrentUser));
        services.Count(d => d.ServiceType == typeof(ICurrentUser)).Should().Be(1);
    }

    [Fact]
    public void The_fail_closed_default_registered_after_identity_does_not_win()
    {
        var services = new ServiceCollection();
        AddIdentity(services);
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        Effective(services).Should().Be(typeof(ClaimsPrincipalCurrentUser));
    }

    [Fact]
    public void An_implementation_the_app_registered_is_kept()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser, AppCurrentUser>();
        AddIdentity(services);

        Effective(services).Should().Be(typeof(AppCurrentUser));
    }
}
