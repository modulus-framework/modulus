namespace Modulus.OpenTelemetry;

using System.Diagnostics;

public static class ModulusActivitySources
{
    public static readonly ActivitySource Mediator
        = new("Modulus.Mediator", "1.0.0");

    public static readonly ActivitySource Events
        = new("Modulus.Events", "1.0.0");

    public static readonly ActivitySource BackgroundJobs
        = new("Modulus.BackgroundJobs", "1.0.0");

    public static readonly string[] All = [
        Mediator.Name,
        Events.Name,
        BackgroundJobs.Name,
    ];
}