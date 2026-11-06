# Machine learning on analytics data with MLflow

MLflow (Apache-2.0, Linux Foundation) tracks experiments, models and their data. Modulus does not depend on it: training jobs read the
lake or the warehouse like any other client, and record which dataset version they used. This runbook sets the rules that keep that
work tenant-scoped and reproducible.

## 1. Read one tenant, one dataset version

- Read the Parquet lake under `{root}/{dataset}_v{n}/tenant={id}/dt=*/`. Read only the tenant's folder; never glob across tenants.
  Filter on the `tenant_id` column too, as `DuckDbLakeReconciler` does, so a misplaced file cannot leak another tenant's rows.
- Read the warehouse with a per-tenant view or row policy, not with a service account that sees every tenant.
- Pin the dataset version in the job. A version is immutable, so a job that names `orders_v2` reads the same contract every time.
  Use the schema the registry holds (`Modulus.Analytics.Apicurio`) to check the columns before training.

## 2. Record what the model saw

Each MLflow run should log:

- the dataset name and version (`orders_v2`), as a run parameter;
- the tenant id, as a tag, if the model is tenant-specific;
- the row count and distinct record count, from `DuckDbLakeReconciler.CountDistinctRecordsAsync`, so a later reader can compare them to the
  queue and the warehouse;
- the schema JSON, as an artifact, from `AnalyticsDatasetSchema.ToJsonSchema`.

Dataset-level lineage is still reported by the backfill's OpenLineage events. An MLflow run cannot appear in that lineage unless the job
also emits its own OpenLineage run event.

## 3. Keep restricted fields out of features

Restricted fields are hashed or removed by the capture pipeline (`EffectiveMasking`). Do not reverse a hash to train on it: a hash is
stable, so it works as an identifier, not as a feature value. Do not copy a `Restricted` column into an MLflow artifact.

## 4. Erasure

When a tenant is erased, its lake files and warehouse rows are removed by the erasure handler. Models trained on that tenant's data are
not retrained automatically. Keep a list of the runs that used a tenant's data (the tag in section 2) so the team can decide whether a
model must be retired.

## 5. What is not covered here

Modulus does not ship an MLflow integration, a model registry or a feature store. This runbook is guidance only, and nothing in it has been
run against a live MLflow server.
