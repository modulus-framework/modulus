#if !NET9_0_OR_GREATER
namespace System.Threading;

/// <summary>Stand-in for <c>System.Threading.Lock</c> (.NET 9+) on net8.0: a monitor-backed lock with the same surface.</summary>
internal sealed class Lock
{
    private readonly object _gate = new();

    public bool IsHeldByCurrentThread => Monitor.IsEntered(_gate);

    public void Enter() => Monitor.Enter(_gate);

    public bool TryEnter() => Monitor.TryEnter(_gate);

    public void Exit() => Monitor.Exit(_gate);

    public Scope EnterScope()
    {
        Enter();
        return new Scope(this);
    }

    public readonly ref struct Scope
    {
        private readonly Lock _owner;

        internal Scope(Lock owner) => _owner = owner;

        public void Dispose() => _owner.Exit();
    }
}
#endif
