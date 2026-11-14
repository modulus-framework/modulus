namespace Modulus.Search;

using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

/// <summary>Registration of the search providers. Each registers <see cref="ISearchIndex"/>: register one per application.</summary>
public static class SearchExtensions
{
    /// <summary>Full-text search over OpenSearch (or any server with its REST API), settings in <c>Search</c>.</summary>
    public static IServiceCollection AddModulusOpenSearch(this IServiceCollection services, IConfiguration configuration)
        => AddProvider<OpenSearchIndex>(services, configuration);

    /// <summary>Full-text search over Apache Solr (standalone cores, created on the server with <c>solr precreate</c>), settings in <c>Search</c>.</summary>
    public static IServiceCollection AddModulusSolr(this IServiceCollection services, IConfiguration configuration)
        => AddProvider<SolrIndex>(services, configuration);

    private static IServiceCollection AddProvider<TProvider>(IServiceCollection services, IConfiguration configuration)
        where TProvider : class, ISearchIndex
    {
        services.AddOptions<SearchOptions>()
            .Bind(configuration.GetSection(SearchOptions.SectionName))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https",
                "Search:BaseUrl must be an absolute http or https address.")
            .Validate(o => string.IsNullOrEmpty(o.Username) || !string.IsNullOrEmpty(o.Password),
                "Search:Password is required with Search:Username.")
            .ValidateOnStart();

        services.AddHttpClient<ISearchIndex, TProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
            var baseUrl = settings.BaseUrl!.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/";
            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            if (!string.IsNullOrEmpty(settings.Username))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        });
        return services;
    }
}
