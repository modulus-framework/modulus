using System.Collections.Concurrent;

namespace Modulus.Messaging.Testing;

/// <summary>
/// Runs the transport contract checks against any <see cref="IMessageTransport"/>. Each check starts a fresh transport
/// from the factory, so checks do not share state. Use it from a transport's test project:
/// <c>var report = await TransportConformance.RunAsync(() => new MyTransport(...));</c> then <c>report.EnsurePassed()</c>.
/// </summary>
public static class TransportConformance
{
    // Each check gets its own endpoint, so a durable transport cannot hand a check the messages a previous check left behind.
    private static string NewEndpoint() => "conformance." + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Runs every check and reports which passed and which failed.</summary>
    public static async Task<TransportConformanceReport> RunAsync(
        Func<IMessageTransport> createTransport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createTransport);

        var checks = new (string Name, Func<Func<IMessageTransport>, CancellationToken, Task> Run)[]
        {
            ("delivers each sent message once", DeliversEachSentMessageOnce),
            ("delivers one endpoint in send order", DeliversOneEndpointInSendOrder),
            ("redelivers the same message after the delay", RedeliversTheSameMessage),
            ("a message moved to error is not delivered again", MovedToErrorIsNotDeliveredAgain),
            ("a message moved to skipped is not delivered again", MovedToSkippedIsNotDeliveredAgain),
            ("stop completes", StopCompletes),
        };

        var passed = new List<string>();
        var failed = new List<string>();
        foreach (var (name, run) in checks)
        {
            try
            {
                await run(createTransport, cancellationToken).ConfigureAwait(false);
                passed.Add(name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        return new TransportConformanceReport(passed, failed);
    }

    private static async Task DeliversEachSentMessageOnce(Func<IMessageTransport> create, CancellationToken ct)
    {
        var received = new ConcurrentQueue<MessageEnvelope>();
        var (transport, envelopes) = await StartAndSendAsync(create, 5, ct, received);
        try
        {
            await WaitForAsync(() => received.Count >= envelopes.Count, ct);
            await Task.Delay(200, ct);

            Check(received.Count == envelopes.Count, $"expected {envelopes.Count} deliveries, got {received.Count}");
            var sent = envelopes.Select(e => e.MessageId).OrderBy(id => id);
            var got = received.Select(e => e.MessageId).OrderBy(id => id);
            Check(sent.SequenceEqual(got), "the delivered message ids differ from the sent ones");
        }
        finally
        {
            await transport.Stop(ct);
        }
    }

    private static async Task DeliversOneEndpointInSendOrder(Func<IMessageTransport> create, CancellationToken ct)
    {
        var received = new ConcurrentQueue<MessageEnvelope>();
        var (transport, envelopes) = await StartAndSendAsync(create, 50, ct, received);
        try
        {
            await WaitForAsync(() => received.Count >= envelopes.Count, ct);

            var order = received.Select(e => ((ConformanceMessage)e.Message).Sequence).ToArray();
            Check(order.SequenceEqual(Enumerable.Range(0, envelopes.Count)), "messages of one endpoint arrived out of send order");
        }
        finally
        {
            await transport.Stop(ct);
        }
    }

    private static async Task RedeliversTheSameMessage(Func<IMessageTransport> create, CancellationToken ct)
    {
        var received = new ConcurrentQueue<MessageEnvelope>();
        var (transport, envelopes) = await StartAndSendAsync(create, 1, ct, received);
        try
        {
            await WaitForAsync(() => received.Count == 1, ct);
            var envelope = envelopes[0];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await transport.Redeliver(envelope, TimeSpan.FromMilliseconds(300), ct);

            await WaitForAsync(() => received.Count == 2, ct);
            Check(received.ToArray()[1].MessageId == envelope.MessageId, "the redelivered message has a different id");
            Check(clock.Elapsed >= TimeSpan.FromMilliseconds(250), "the redelivery came before its delay");
        }
        finally
        {
            await transport.Stop(ct);
        }
    }

    private static async Task MovedToErrorIsNotDeliveredAgain(Func<IMessageTransport> create, CancellationToken ct)
    {
        var received = new ConcurrentQueue<MessageEnvelope>();
        var (transport, envelopes) = await StartAndSendAsync(create, 1, ct, received);
        try
        {
            await WaitForAsync(() => received.Count == 1, ct);
            await transport.MoveToError(envelopes[0], new InvalidOperationException("conformance"), ct);

            await Task.Delay(300, ct);
            Check(received.Count == 1, "a message moved to error was delivered again");
        }
        finally
        {
            await transport.Stop(ct);
        }
    }

    private static async Task MovedToSkippedIsNotDeliveredAgain(Func<IMessageTransport> create, CancellationToken ct)
    {
        var received = new ConcurrentQueue<MessageEnvelope>();
        var (transport, envelopes) = await StartAndSendAsync(create, 1, ct, received);
        try
        {
            await WaitForAsync(() => received.Count == 1, ct);
            await transport.MoveToSkipped(envelopes[0], ct);

            await Task.Delay(300, ct);
            Check(received.Count == 1, "a message moved to skipped was delivered again");
        }
        finally
        {
            await transport.Stop(ct);
        }
    }

    private static async Task StopCompletes(Func<IMessageTransport> create, CancellationToken ct)
    {
        var transport = create();
        await transport.Start([NewEndpoint()], (_, _) => Task.CompletedTask, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await transport.Stop(timeout.Token);
    }

    private static async Task<(IMessageTransport Transport, List<MessageEnvelope> Envelopes)> StartAndSendAsync(
        Func<IMessageTransport> create,
        int count,
        CancellationToken ct,
        ConcurrentQueue<MessageEnvelope> received)
    {
        var transport = create();
        var endpoint = NewEndpoint();
        await transport.Start(
            [endpoint],
            (envelope, _) =>
            {
                received.Enqueue(envelope);
                return Task.CompletedTask;
            },
            ct);

        var envelopes = new List<MessageEnvelope>();
        for (var i = 0; i < count; i++)
        {
            var envelope = new MessageEnvelope(Guid.NewGuid(), endpoint, new ConformanceMessage(i), new Dictionary<string, string>());
            envelopes.Add(envelope);
            await transport.Send(envelope, ct);
        }

        return (transport, envelopes);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        if (!await MessagingTestHarness.WaitUntilAsync(condition, TimeSpan.FromSeconds(10)).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("the transport did not deliver in time");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>The payload the checks send; its sequence number lets the order check see arrival order.</summary>
    private sealed record ConformanceMessage(int Sequence);
}

/// <summary>The outcome of <see cref="TransportConformance.RunAsync"/>.</summary>
/// <param name="Passed">Names of the checks that passed.</param>
/// <param name="Failed">Each failed check with its reason.</param>
public sealed record TransportConformanceReport(IReadOnlyList<string> Passed, IReadOnlyList<string> Failed)
{
    /// <summary>Throws listing every failed check when any check failed.</summary>
    public void EnsurePassed()
    {
        if (Failed.Count > 0)
        {
            throw new InvalidOperationException("Transport conformance failed: " + string.Join("; ", Failed));
        }
    }
}
