# Lakehouse runbook: Iceberg tables, Spark and Trino

Operator procedure for the lakehouse side of the analytics layer: the Parquet files the `Modulus.Analytics.Parquet` sink writes become Apache Iceberg
tables, which Spark maintains and Trino queries. Modulus does not write Iceberg metadata itself: no mature .NET writer exists, and the table format's own
engines commit the tables.

## 1. The pattern

```
Modulus.Analytics.Parquet  ->  object storage (tenant=/dt= layout, one file per batch)
                                  |
                 Spark (or the Iceberg Kafka Connect sink) commits Iceberg snapshots
                                  |
                 Iceberg catalog (REST: Apache Polaris or Project Nessie)
                                  |
                 Trino (queries, row-level deletes for erasure)
```

The Parquet sink writes immutable files under `{root}/{dataset}_v{n}/tenant={id}/dt={yyyy-MM-dd}/part-{hash}.parquet`, and the dataset's JSON Schema
beside them under `{root}/_schemas/`. A file's name is a hash of its record ids, so a retried batch with the same records overwrites its own file
instead of adding a second one.

## 2. Committing the files

Choose one of the two paths:

- **Spark** (batch, a scheduled job): register the files as an Iceberg table with `add_files` (or write them with `INSERT` from a view over the
  Parquet folder), then run `rewrite_data_files` and `expire_snapshots` on a schedule. Use the Iceberg Spark runtime that matches your Spark release.
- **Kafka Connect Iceberg sink** (continuous): when the batches are also published to Kafka, the Apache Iceberg Kafka Connect sink commits them as they
  arrive. Its configuration keys change between releases: check them against the release you deploy.

Either way, the catalog is a REST catalog (Apache Polaris or Project Nessie, both Apache-2.0). Configure it once, and point every engine at it.

## 3. Querying with Trino

Trino's Iceberg connector reads the same catalog. A catalog file (`etc/catalog/lake.properties`) names the REST catalog and the object store:

```properties
connector.name=iceberg
iceberg.catalog.type=rest
iceberg.rest-catalog.uri=http://polaris:8181/api/catalog
iceberg.rest-catalog.warehouse=analytics
```

Tenant isolation is a query rule, not a file layout: a query for one company filters on `tenant_id`. Give analysts a view that does, rather than the raw
table, when they must not see other companies.

## 4. Erasure and retention in the lake

The Parquet sink cannot delete one company's files: object storage has no listing through the framework's `IFileStorage`. Erasure is therefore done
with the table format:

```sql
DELETE FROM lake.analytics.app_orders_v1 WHERE tenant_id = '<company id>';
```

Run it in Trino (row-level deletes on Iceberg v2 tables), then expire the old snapshots so the deleted rows leave the storage:
`ALTER TABLE ... EXECUTE expire_snapshots(retention_threshold => '7d')`. Retention for old data is the same mechanism, with a `WHERE occurred_at < ...`
delete on the schedule the legal period sets.

## 5. What is verified

The Parquet sink's file layout and writer are unit-tested (`Modulus.Analytics.Parquet.Tests`). The Iceberg commit, Spark and Trino steps above are
operator procedures: they were not run in this environment. Check the Iceberg and Trino configuration keys against the versions you deploy.
