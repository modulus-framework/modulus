namespace Modulus.Data.Cassandra;

public sealed class CassandraOptions
{
    public string[] ContactPoints { get; set; } = ["localhost"];
    public int      Port          { get; set; } = 9042;
    public string   Keyspace      { get; set; } = default!;
    public string   Datacenter    { get; set; } = "datacenter1";
    public string   TablePrefix   { get; set; } = string.Empty;
}