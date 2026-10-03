namespace Modulus.Platform.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Modulus.Core.Abstractions;
using Modulus.SignalR;
using NSubstitute;
using Xunit;

/// <summary>Security plan 3b: a tenant-scoped SignalR group never forms without a company in scope.</summary>
[Trait("Category", "Unit")]
public sealed class ModulusHubTenantGroupTests
{
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private ProbeHub Hub()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("c1");
        return new ProbeHub(Substitute.For<ICurrentUser>(), _tenant) { Context = context, Groups = _groups };
    }

    [Fact]
    public async Task Joining_prefixes_the_group_with_the_company()
    {
        var company = Guid.NewGuid();
        _tenant.IsAvailable.Returns(true);
        _tenant.TenantId.Returns(company);

        await Hub().Join("orders");

        await _groups.Received(1).AddToGroupAsync("c1", $"tenant:{company}:orders", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_company_joining_and_leaving_are_refused(bool host)
    {
        // No tenant would otherwise build "tenant::orders", one group shared by every tenant-less caller.
        _tenant.IsHost.Returns(host);
        var hub = Hub();

        await FluentActions.Awaiting(() => hub.Join("orders")).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => hub.Leave("orders")).Should().ThrowAsync<InvalidOperationException>();
        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    public interface IProbeClient;

    private sealed class ProbeHub(ICurrentUser user, ICurrentTenant tenant) : ModulusHub<IProbeClient>(user, tenant)
    {
        public Task Join(string group) => JoinTenantGroupAsync(group);

        public Task Leave(string group) => LeaveTenantGroupAsync(group);
    }
}
