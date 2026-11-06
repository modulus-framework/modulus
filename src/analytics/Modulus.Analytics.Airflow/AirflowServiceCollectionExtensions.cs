namespace Modulus.Analytics.Airflow;

using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Registers the Airflow trigger: completed backfill runs start the transform DAG.</summary>
public static partial class AirflowServiceCollectionExtensions
{
    /// <summary>
    /// Starts the transform DAG (settings from <c>Analytics:Airflow</c>, <see cref="AirflowOptions"/>) for every completed backfill run. Settings are validated
    /// at startup when the trigger is enabled. Basic authentication is used unless an API token is set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds the section.</param>
    public static IServiceCollection AddModulusAirflow(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AirflowOptions>()
            .Bind(configuration.GetSection(AirflowOptions.SectionName))
            .Validate(o => !o.Enabled || o.BaseUrl is { IsAbsoluteUri: true, Scheme: "http" or "https" },
                "Analytics:Airflow:BaseUrl must be an absolute http or https URL when Airflow is enabled.")
            .Validate(o => DagIdPattern().IsMatch(o.DagId ?? string.Empty),
                "Analytics:Airflow:DagId must be 1 to 250 letters, digits, dots, dashes or underscores.")
            .Validate(o => o.Timeout > TimeSpan.Zero, "Analytics:Airflow:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient(AirflowRunObserver.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<AirflowOptions>>().Value;
                client.Timeout = settings.Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrEmpty(settings.ApiToken))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);
                }
                else if (!string.IsNullOrEmpty(settings.Username))
                {
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }
            });

        services.AddSingleton<IAnalyticsRunObserver, AirflowRunObserver>();
        return services;
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,250}$")]
    private static partial Regex DagIdPattern();
}
