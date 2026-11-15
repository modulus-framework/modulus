using System.Diagnostics;

namespace Modulus.Messaging.Chaos.Tests;

/// <summary>Runs the worker processes. A kill is a process kill: nothing is stopped gracefully, so leases are left to expire.</summary>
internal sealed class WorkerPool(string connection, string endpoint, int count)
{
    private readonly Process?[] _processes = new Process?[count];
    private readonly string _dll = Path.Combine(AppContext.BaseDirectory, "Modulus.Messaging.Chaos.Worker.dll");

    internal int Kills { get; private set; }

    internal int Starts { get; private set; }

    internal void StartAll()
    {
        for (var slot = 0; slot < _processes.Length; slot++)
        {
            StartSlot(slot);
        }
    }

    /// <summary>Kills one running worker at random. Returns false when none is running.</summary>
    internal bool KillOne(Random random)
    {
        var running = Enumerable.Range(0, _processes.Length).Where(slot => _processes[slot] is { HasExited: false }).ToArray();
        if (running.Length == 0)
        {
            return false;
        }

        var slot = running[random.Next(running.Length)];
        var process = _processes[slot]!;
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        process.Dispose();
        _processes[slot] = null;
        Kills++;
        return true;
    }

    /// <summary>Starts a worker in every slot that has none.</summary>
    internal void RestartDead()
    {
        for (var slot = 0; slot < _processes.Length; slot++)
        {
            if (_processes[slot] is null or { HasExited: true })
            {
                StartSlot(slot);
            }
        }
    }

    internal void StopAll()
    {
        foreach (var process in _processes)
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            process?.Dispose();
        }

        Array.Clear(_processes);
    }

    private void StartSlot(int slot)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(_dll);
        start.ArgumentList.Add(connection);
        start.ArgumentList.Add(endpoint);
        _processes[slot] = Process.Start(start) ?? throw new InvalidOperationException("The worker process did not start.");
        Starts++;
    }
}
