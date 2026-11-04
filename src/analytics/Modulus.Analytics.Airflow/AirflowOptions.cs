namespace Modulus.Analytics.Airflow;

/// <summary>Settings of the Airflow trigger (section <c>Analytics:Airflow</c>).</summary>
public sealed class AirflowOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Airflow";

    /// <summary>Whether completed backfill runs start the DAG. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Airflow web server, for example <c>http://airflow-webserver:8080</c>. Required when <see cref="Enabled"/> is set.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// The DAG that transforms the warehouse tables. It receives the run in its configuration and runs the dbt models it selects (see
    /// <see cref="DbtSelector"/>).
    /// </summary>
    public string DagId { get; set; } = "modulus_analytics_transform";

    /// <summary>The dbt selection passed to the DAG (for example <c>tag:modulus_analytics</c>), so the DAG runs only the models for analytics.</summary>
    public string DbtSelector { get; set; } = "tag:modulus_analytics";

    /// <summary>The Airflow user for basic authentication. Keep the password in a secret store, not in a committed file.</summary>
    public string? Username { get; set; }

    /// <summary>The password of <see cref="Username"/>.</summary>
    public string? Password { get; set; }

    /// <summary>A bearer token for the Airflow API, used instead of basic authentication when set.</summary>
    public string? ApiToken { get; set; }

    /// <summary>How long one trigger request may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
