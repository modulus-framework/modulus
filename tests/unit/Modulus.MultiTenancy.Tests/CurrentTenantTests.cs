using FluentAssertions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

[Trait("Category", "Unit")]
public sealed class CurrentTenantTests
{
    [Fact]
    public void Default_State_HasNoTenant()
    {
        var tenant = new CurrentTenant();

        tenant.IsAvailable.Should().BeFalse();
        tenant.TenantId.Should().BeNull();
        tenant.TenantSlug.Should().BeNull();
    }

    [Fact]
    public void Change_SetsTenant_ForScope()
    {
        var tenant = new CurrentTenant();
        var info = new TenantInfo(Guid.NewGuid(), "acme", "Acme Corp");

        using (tenant.Change(info))
        {
            tenant.IsAvailable.Should().BeTrue();
            tenant.TenantId.Should().Be(info.TenantId);
            tenant.TenantSlug.Should().Be("acme");
        }

        // Scope exited → restored to no-tenant.
        tenant.IsAvailable.Should().BeFalse();
        tenant.TenantId.Should().BeNull();
    }

    [Fact]
    public void Change_WithNull_SwitchesToHostContext()
    {
        var tenant = new CurrentTenant();
        var info = new TenantInfo(Guid.NewGuid(), "acme");

        using (tenant.Change(info))
        {
            tenant.IsAvailable.Should().BeTrue();
            using (tenant.Change(null))
            {
                // Explicit host context.
                tenant.IsAvailable.Should().BeFalse();
                tenant.TenantId.Should().BeNull();
            }
            // Inner scope disposed → outer tenant restored.
            tenant.TenantId.Should().Be(info.TenantId);
        }
    }

    [Fact]
    public void Unresolved_IsNotHost_FailClosed()
    {
        // The core fail-closed distinction: an unresolved tenant must NOT report
        // as host, or query filters would match every tenant's rows.
        var tenant = new CurrentTenant();

        tenant.IsHost.Should().BeFalse();
        tenant.TenantId.Should().BeNull();
    }

    [Fact]
    public void ExplicitHostScope_IsHost_ResolvedTenant_IsNot()
    {
        var tenant = new CurrentTenant();
        var info = new TenantInfo(Guid.NewGuid(), "acme");

        using (tenant.Change(null))
            tenant.IsHost.Should().BeTrue("Change(null) is the deliberate host scope");

        using (tenant.Change(info))
            tenant.IsHost.Should().BeFalse("a resolved tenant is never host");

        // Restored to unresolved → fail-closed again.
        tenant.IsHost.Should().BeFalse();
    }

    [Fact]
    public void Nested_Changes_RestoreInLifoOrder()
    {
        var tenant = new CurrentTenant();
        var a = new TenantInfo(Guid.NewGuid(), "a");
        var b = new TenantInfo(Guid.NewGuid(), "b");

        using (tenant.Change(a))
        {
            tenant.TenantId.Should().Be(a.TenantId);
            using (tenant.Change(b))
            {
                tenant.TenantId.Should().Be(b.TenantId);
            }
            tenant.TenantId.Should().Be(a.TenantId);
        }
        tenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Change_FlowsAcrossAsyncBoundaries()
    {
        // The whole point of AsyncLocal: a tenant established in a task must
        // be visible to awaited continuations on that flow.
        var tenant = new CurrentTenant();
        var info = new TenantInfo(Guid.NewGuid(), "acme");

        using (tenant.Change(info))
        {
            await Task.Yield();
            tenant.TenantId.Should().Be(info.TenantId);

            await SomeAsyncWork(tenant);
        }

        tenant.IsAvailable.Should().BeFalse();

        static async Task SomeAsyncWork(ICurrentTenant t)
        {
            await Task.Yield();
            t.TenantId.Should().NotBeNull();
            t.IsAvailable.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Change_InOneTask_IsNotVisibleToParallelSibling()
    {
        // AsyncLocal is flow-relative: a tenant set on one async flow must
        // NOT leak into a parallel sibling flow. This is the property that
        // keeps concurrent requests/tenants isolated.
        var tenant = new CurrentTenant();
        var acme = new TenantInfo(Guid.NewGuid(), "acme");

        var acmeSeen = false;
        var otherSeenAs = (Guid?)null;

        var t1 = Task.Run(() =>
        {
            using (tenant.Change(acme))
            {
                Thread.Sleep(20); // overlap with t2
                acmeSeen = tenant.TenantId == acme.TenantId;
            }
        });

        var t2 = Task.Run(() =>
        {
            Thread.Sleep(20); // overlap with t1
            otherSeenAs = tenant.TenantId; // should NOT see acme
        });

        await Task.WhenAll(t1, t2);

        acmeSeen.Should().BeTrue("the owning flow must see its own tenant");
        otherSeenAs.Should().BeNull("a sibling flow must not observe another flow's tenant");
    }
}
