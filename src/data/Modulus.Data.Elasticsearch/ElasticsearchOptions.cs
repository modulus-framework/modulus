namespace Modulus.Data.Elasticsearch;

public sealed class ElasticsearchOptions
{
    public string  Url                  { get; set; } = "http://localhost:9200";
    public string? Username             { get; set; }
    public string? Password             { get; set; }
    public string? CertificateFingerprint { get; set; }
    public string  IndexPrefix          { get; set; } = string.Empty;
}