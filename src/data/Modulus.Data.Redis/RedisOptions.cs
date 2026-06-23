namespace Modulus.Data.Redis;

public sealed class RedisOptions
{
    public string    ConnectionString { get; set; } = default!;
    public string    KeyPrefix        { get; set; } = string.Empty;
    public TimeSpan? DefaultTtl       { get; set; }
}