namespace Modulus.Outbox.Abstractions;

public sealed class OutboxOptions
{
    public int    BatchSize          { get; set; } = 100;
    public int    MaxRetries         { get; set; } = 5;
    public int    PollingIntervalSec { get; set; } = 5;
    public string Dispatcher         { get; set; } = "in-process";
    public bool   DisableAutoPolling { get; set; } = false;
}