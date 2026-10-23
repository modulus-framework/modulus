using Modulus.Core.Abstractions;

namespace Modulus.Identity.Tests;

/// <summary>The company in scope for the current async flow. Nothing in scope is the host, the way a request without a company is.</summary>
internal sealed class SwitchableTenant : ICurrentTenant
{
    private static readonly AsyncLocal<Scope?> s_current = new();

    public Guid? TenantId => s_current.Value?.Id;

    public string? TenantSlug => TenantId?.ToString();

    public bool IsAvailable => TenantId is not null;

    public bool IsHost => TenantId is null;

    public IDisposable Change(TenantInfo? tenant)
    {
        var previous = s_current.Value;
        s_current.Value = new Scope(tenant?.TenantId);
        return new Restore(() => s_current.Value = previous);
    }

    private sealed record Scope(Guid? Id);

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
