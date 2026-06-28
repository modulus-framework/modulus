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
    private static readonly AsyncLocal<TenantInfo?> _current = new();

    public Guid? TenantId => _current.Value?.TenantId;
    public string? TenantSlug => _current.Value?.TenantSlug;
    public bool IsAvailable => _current.Value is not null;

    /// <summary>
    /// Back-compat write surface for <see cref="TenantMiddleware"/>, which
    /// resolves the scoped instance once per request. Sets the ambient
    /// tenant for the remainder of the current async flow.
    /// </summary>
    internal void Set(TenantInfo info) => _current.Value = info;

    /// <summary>
    /// Establishes <paramref name="tenant"/> as the ambient tenant for the
    /// current async flow and returns a scope that restores the previous
    /// value when disposed. Designed for background work:
    /// <code>using var _ = currentTenant.Change(tenant);</code>
    /// </summary>
    public IDisposable Change(TenantInfo? tenant)
        => new TenantScope(_current, tenant);

    private sealed class TenantScope : IDisposable
    {
        private readonly AsyncLocal<TenantInfo?> _state;
        private readonly TenantInfo? _previous;
        private int _disposed;

        public TenantScope(AsyncLocal<TenantInfo?> state, TenantInfo? value)
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
