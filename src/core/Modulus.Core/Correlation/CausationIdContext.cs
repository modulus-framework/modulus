namespace Modulus.Core.Correlation;

using Modulus.Core.Abstractions;

/// <summary>
/// <see cref="ICausationIdContext"/> backed by a static <see cref="AsyncLocal{T}"/>,
/// mirroring <see cref="CorrelationContext"/>: a causation id set on one async flow
/// (message consumer) is visible to all its continuations while staying invisible to
/// unrelated flows. Register as a <b>singleton</b>.
/// </summary>
public sealed class CausationIdContext : ICausationIdContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public string? CausationId => _current.Value;

    public bool IsSet => _current.Value is not null;

    public IDisposable BeginScope(string causationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(causationId);
        return new Scope(causationId);
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
