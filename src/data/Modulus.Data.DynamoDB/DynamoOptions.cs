namespace Modulus.Data.DynamoDB;

public sealed class DynamoOptions
{
    public string Region      { get; set; } = "us-east-1";
    public string TablePrefix { get; set; } = string.Empty;
    // ServiceURL used for DynamoDB Local in development
    public string? ServiceUrl { get; set; }
}