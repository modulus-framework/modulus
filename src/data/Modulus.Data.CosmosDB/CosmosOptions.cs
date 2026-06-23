namespace Modulus.Data.CosmosDB;

public sealed class CosmosOptions
{
    public string AccountEndpoint  { get; set; } = default!;
    public string AccountKey       { get; set; } = default!;
    public string DatabaseId       { get; set; } = default!;
    public string ContainerPrefix  { get; set; } = string.Empty;
    public string PartitionKeyPath { get; set; } = "/tenantId";
}