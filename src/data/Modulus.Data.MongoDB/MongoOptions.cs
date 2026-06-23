namespace Modulus.Data.MongoDB;

public sealed class MongoOptions
{
    public string ConnectionString    { get; set; } = default!;
    public string DatabaseName        { get; set; } = default!;
    public string CollectionPrefix    { get; set; } = string.Empty;
}