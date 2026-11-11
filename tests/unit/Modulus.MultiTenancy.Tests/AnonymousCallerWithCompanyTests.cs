using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.MultiTenancy.Extensions;
using Modulus.MultiTenancy.Resolvers;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// An anonymous request that names a company makes the tenant middleware try the other authentication schemes. A scheme that cannot
/// read a request of that kind (the OpenIddict server, for a non-token endpoint) throws; that used to turn every such request into a
/// 500. The scheme now contributes no principal, so the caller stays anonymous and the endpoint's policy decides.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AnonymousCallerWithCompanyTests
{
    private static readonly TenantInfo Company = new(Guid.NewGuid(), "company");

    [Fact]
    public async Task An_anonymous_request_naming_a_company_reaches_the_endpoint_when_a_scheme_throws()
    {
        var store = new FakeStore(Company);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.RequireMembership());
        services.AddSingleton<CurrentTenant>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ThrowingHandler>(ThrowingHandler.SchemeName, _ => { });

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Headers["X-Tenant-Id"] = Company.TenantId.ToString();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        var reached = false;

        IEnumerable<ITenantResolver> resolvers = [new HeaderTenantResolver(store)];
        var middleware = new TenantMiddleware(_ => { reached = true; return Task.CompletedTask; }, resolvers, NullLogger<TenantMiddleware>.Instance);
        await middleware.InvokeAsync(ctx);

        reached.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    private sealed class ThrowingHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggers,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
    {
        public const string SchemeName = "ThrowsForThisRequest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => throw new InvalidOperationException("An identity cannot be extracted from this request.");
    }

    private sealed class FakeStore(params TenantInfo[] tenants) : ITenantStore
    {
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(tenants.FirstOrDefault(t => t.TenantId == id));

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
            => Task.FromResult(tenants.FirstOrDefault(t => t.TenantSlug == slug));
    }
}
