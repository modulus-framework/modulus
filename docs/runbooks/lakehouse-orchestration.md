# Lakehouse runbook: orchestration (Airflow and dbt) and BI (Superset)

Operator procedure for `Modulus.Analytics.Airflow` and `Modulus.Analytics.Superset`.

## 1. Transform after a backfill: Airflow and dbt

When a backfill run of a dataset completes for a company, the application starts an Airflow DAG through the stable REST API
(`POST /api/v1/dags/{dag_id}/dagRuns`). The DAG transforms the warehouse tables with dbt.

Settings (`Analytics:Airflow`):

```json
{
  "Analytics": {
    "Airflow": {
      "Enabled": true,
      "BaseUrl": "http://airflow-webserver:8080",
      "DagId": "modulus_analytics_transform",
      "DbtSelector": "tag:modulus_analytics",
      "Username": "modulus",
      "Password": "from-a-secret-store"
    }
  }
}
```

Register it with `builder.Services.AddModulusAirflow(builder.Configuration);`.

What the trigger sends:

- `dag_run_id`: `modulus-<run id>`. A retried trigger for the same run is refused by Airflow with `409`, which the application counts as started.
- `conf`: `dataset`, `version`, `tenant_id`, `run_id`, `rows_queued` and `dbt_select`. The DAG uses `dbt_select` to build only the models tagged for
  analytics, for example with `dbt build --select tag:modulus_analytics`.

The DAG itself is the operator's: a task that runs `dbt build` with the selector from the configuration, against the warehouse. dbt Core has no REST API,
so Airflow is the trigger. If Airflow is down, the trigger fails and is logged; the backfill's rows are already queued and the run stays completed. Start
the DAG by hand for the affected runs (the `run_id` in `conf` identifies them).

Check the settings at startup: `BaseUrl` must be an absolute http or https URL when enabled, and `DagId` may contain only letters, digits, dots, dashes
and underscores.

## 2. Embedded dashboards: Superset guest tokens

`Modulus.Analytics.Superset` requests a guest token for one embedded dashboard, limited to one company's rows through a row-level-security rule
(`tenant_id = '<company id>'`). The token goes to the embedding page; the page never holds the service account's password.

Settings (`Analytics:Superset`):

```json
{
  "Analytics": {
    "Superset": {
      "BaseUrl": "https://superset.example.org",
      "Username": "analytics-service",
      "Password": "from-a-secret-store",
      "TenantColumn": "tenant_id"
    }
  }
}
```

Superset setup, once:

1. Create the service account with the `can_grant_guest_token` permission. Without it, Superset refuses every token.
2. Set the guest token lifetime in Superset's configuration (`GUEST_TOKEN_JWT_EXP_SECONDS`). Tokens are not cached by the application; each request gets
   a fresh one.
3. Serve Superset over TLS. A guest token is a bearer credential for a company's data.
4. Give each dataset the tenant column, so the row-level rule applies to every chart on the dashboard.

Use a dashboard id that is a number, a uuid or a slug (letters, digits, dashes, underscores; up to 64 characters). Other values are refused before any
request is sent.

## 3. What is verified

The Airflow trigger and the Superset helper are unit-tested over real HTTP against fake Airflow and Superset servers: the requests, the authentication,
the idempotent `409`, the refused sign-in without the password in the message, the refused identifiers, and the startup checks. A live Airflow, dbt or
Superset was not run in this environment.
