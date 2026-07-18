namespace Modulus.MultiTenancy;

using Modulus.Core.Abstractions;

/// <summary>
/// Backed by a static <see cref="AsyncLocal{T}"/> so that a tenant established
/// on one async flow (a request, a background job, a message consumer) is
/// visible to all continuations on that flow, while remaining invisible to
/// unrelated flows. This is what lets tenant context propagate into hosted
/// services and message-bus consumers that open their own DI scope — which a
/// plain scoped POCO cannot do.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    // The ambient state distinguishes three cases that a bare TenantInfo? cannot:
    //   * null            → unresolved (fail-closed: not host, no tenant)
    //   * State(t, false) → tenant t resolved
    //   * State(null,true)→ explicit host scope (Change(null))
    private static readonly AsyncLocal<TenantState?> _current = new();

    private sealed record TenantState(TenantInfo? Tenant, bool IsHost);

    public Guid? TenantId => _current.Value?.Tenant?.TenantId;
    public string? TenantSlug => _current.Value?.Tenant?.TenantSlug;
    public bool IsAvailable => _current.Value?.Tenant is not null;

    /// <summary>
    /// True only in an <b>explicit</b> host scope (<c>Change(null)</c>). An
    /// unresolved tenant is not host — it is fail-closed. See
    /// <see cref="ICurrentTenant.IsHost"/>.
    /// </summary>
    public bool IsHost => _current.Value?.IsHost ?? false;

    /// <summary>
    /// Back-compat write surface for <see cref="TenantMiddleware"/>, which
    /// resolves the scoped instance once per request. Sets the ambient
    /// tenant for the remainder of the current async flow.
    /// </summary>
    internal void Set(TenantInfo info)
        => _current.Value = new TenantState(info, IsHost: false);

    /// <summary>
    /// Establishes <paramref name="tenant"/> as the ambient tenant for the
    /// current async flow and returns a scope that restores the previous
    /// value when disposed. Designed for background work:
    /// <code>using var _ = currentTenant.Change(tenant);</code>
    /// Passing <see langword="null"/> enters the explicit host scope
    /// (<see cref="IsHost"/> becomes <see langword="true"/>).
    /// </summary>
    public IDisposable Change(TenantInfo? tenant)
        => new TenantScope(
            _current,
            tenant is null ? new TenantState(null, IsHost: true)
                           : new TenantState(tenant, IsHost: false));

    private sealed class TenantScope : IDisposable
    {
        private readonly AsyncLocal<TenantState?> _state;
        private readonly TenantState? _previous;
        private int _disposed;

        public TenantScope(AsyncLocal<TenantState?> state, TenantState? value)
        {
            _state = state;
            _previous = state.Value;
            state.Value = value;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _state.Value = _previous;
        }
    }
}
