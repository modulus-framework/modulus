namespace Modulus.Data.Elasticsearch.Extensions;

using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Options;

public static class ElasticsearchServiceCollectionExtensions
{
    public static IServiceCollection AddElasticsearch(
        this IServiceCollection services,
        Action<ElasticsearchOptions> configure)
    {
        var opts = new ElasticsearchOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<ElasticsearchClient>(_ =>
        {
            var settings = new ElasticsearchClientSettings(
                new Uri(opts.Url));

            if (opts.Username is not null && opts.Password is not null)
                settings.Authentication(
                    new BasicAuthentication(opts.Username, opts.Password));

            if (opts.CertificateFingerprint is not null)
                settings.CertificateFingerprint(opts.CertificateFingerprint);

            return new ElasticsearchClient(settings);
        });

        services.AddScoped<ElasticsearchIndexManager>();
        return services;
    }
}