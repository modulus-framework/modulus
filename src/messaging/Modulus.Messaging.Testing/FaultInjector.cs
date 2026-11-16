namespace Modulus.Messaging.Testing;

/// <summary>
/// Makes consumers fail on purpose (FR-25): the next deliveries of a message type throw the given exception before the
/// consumer runs. The failure goes through the bus's retry, redelivery and park rules, as a real consumer failure does.
/// The harness registers one injector; configure it with <see cref="FailNext{TMessage}"/>.
/// </summary>
public sealed class FaultInjector : IMessageFilter
{
    private readonly object _gate = new();
    private readonly List<Rule> _rules = [];

    /// <summary>Fails the next <paramref name="times"/> consume attempts of <typeparamref name="TMessage"/> with <paramref name="exception"/>.</summary>
    public void FailNext<TMessage>(int times, Exception exception)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        lock (_gate)
        {
            _rules.Add(new Rule(typeof(TMessage), times, exception));
        }
    }

    /// <summary>Fails every consume attempt of <typeparamref name="TMessage"/> until <see cref="Clear"/>.</summary>
    public void FailAlways<TMessage>(Exception exception)
        where TMessage : class =>
        FailNext<TMessage>(int.MaxValue, exception);

    /// <summary>Removes every rule.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _rules.Clear();
        }
    }

    /// <summary>How many injected failures are still to come for <typeparamref name="TMessage"/>.</summary>
    public int Remaining<TMessage>()
        where TMessage : class
    {
        lock (_gate)
        {
            return _rules.Where(r => r.Type == typeof(TMessage)).Sum(r => r.Times);
        }
    }

    async Task IMessageFilter.Execute(MessageFilterContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.Point == MessageFilterPoint.Consume)
        {
            var failure = Take(context.Envelope.Message);
            if (failure is not null)
            {
                throw failure;
            }
        }

        await next().ConfigureAwait(false);
    }

    private Exception? Take(object message)
    {
        lock (_gate)
        {
            foreach (var rule in _rules)
            {
                if (rule.Times > 0 && rule.Type.IsInstanceOfType(message))
                {
                    rule.Times--;
                    return rule.Exception;
                }
            }
        }

        return null;
    }

    private sealed class Rule(Type type, int times, Exception exception)
    {
        public Type Type { get; } = type;

        public int Times { get; set; } = times;

        public Exception Exception { get; } = exception;
    }
}
