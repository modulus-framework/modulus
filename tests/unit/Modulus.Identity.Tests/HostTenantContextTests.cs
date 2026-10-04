namespace Modulus.Identity.Tests;

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Xunit;

/// <summary>
/// With multi-tenancy on, the Identity filters hide every account from a context that is not the host, and a token
/// request selects no company: nobody could sign in. The token server's own actions run in the host context.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HostTenantContextTests
{
    [Theory]
    [InlineData(typeof(ModulusTokenController))]
    [InlineData(typeof(ModulusAuthorizeController))]
    [InlineData(typeof(AccountController<ModulusUser>))]
    [InlineData(typeof(ModulusEndSessionController))]
    public void Account_endpoints_run_in_the_host_context(Type controller)
        => controller.GetCustomAttribute<HostTenantContextAttribute>(inherit: true).Should().NotBeNull();

    [Fact]
    public async Task The_filter_enters_the_host_for_the_action_and_restores_the_caller_context_after()
    {
        var tenant = new AmbientTenant();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<ICurrentTenant>(tenant).BuildServiceProvider(),
        };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), controller: new object());
        var company = Guid.NewGuid();
        bool? hostInAction = null;

        using (tenant.Change(new TenantInfo(company, "acme")))
        {
            await new HostTenantContextAttribute().OnActionExecutionAsync(executing, () =>
            {
                hostInAction = tenant.IsHost;
                return Task.FromResult(new ActionExecutedContext(action, [], new object()));
            });

            tenant.TenantId.Should().Be(company);
        }

        hostInAction.Should().BeTrue();
    }

    private sealed class AmbientTenant : ICurrentTenant
    {
        private static readonly AsyncLocal<(Guid? Id, bool Host)> s_current = new();

        public Guid? TenantId => s_current.Value.Id;

        public string? TenantSlug => TenantId?.ToString();

        public bool IsAvailable => TenantId is not null;

        public bool IsHost => s_current.Value.Host;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = s_current.Value;
            s_current.Value = tenant is null ? (null, true) : (tenant.TenantId, false);
            return new Restore(() => s_current.Value = previous);
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }
}
