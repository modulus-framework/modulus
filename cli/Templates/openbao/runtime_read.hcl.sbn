# Runtime identity (the Modulus API host). Read-only, and only under the tenant-database namespace.
# Replace modulus-prod with the environment's KV v2 mount. Staging gets its own mount, policy and role.
#
# Reading a KV v2 secret goes to <mount>/data/<path>; the metadata path is not granted, so the API cannot list
# secrets or read version history.

path "modulus-prod/data/tenant-db/*" {
  capabilities = ["read"]
}
