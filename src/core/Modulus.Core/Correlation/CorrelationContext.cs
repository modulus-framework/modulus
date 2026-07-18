namespace Modulus.Core.Correlation;

using Modulus.Core.Abstractions;

/// <summary>
/// <see cref="ICorrelationContext"/> backed by a static
/// <see cref="AsyncLocal{T}"/>, mirroring <c>CurrentTenant</c>: a correlation id
/// set on one async flow (request / job / consumer) is visible to all its
/// continuations — including code that opens its own DI scope — while staying
/// invisible to unrelated flows. Register as a <b>singleton</b> so the outbound
/// <see cref="System.Net.Http.DelegatingHandler"/> (pooled with the message
/// handler) can safely depend on it.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public string? CorrelationId => _current.Value;

    public bool IsSet => _current.Value is not null;

    public IDisposable BeginScope(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new Scope(correlationId);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private int _disposed;

        public Scope(string value)
        {
            _previous = _current.Value;
            _current.Value = value;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _current.Value = _previous;
        }
    }
}
