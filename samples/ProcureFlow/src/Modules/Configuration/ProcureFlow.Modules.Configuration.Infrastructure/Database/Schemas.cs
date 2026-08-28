namespace ProcureFlow.Modules.Configuration.Infrastructure.Database;

internal static class Schemas
{
    public const string Configuration = "configuration";

    // Legacy names kept for the entity configurations; Settings and FeatureFlags
    // live in the single `configuration` schema.
    public const string Settings = Configuration;
    public const string Features = Configuration;
}