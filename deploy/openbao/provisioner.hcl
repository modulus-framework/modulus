# Provisioning identity (the tenant provisioning worker). Writes only the tenant-database namespace; no administration.
#
# create/update write new versions (check-and-set). read covers the provisioner's own verification and the
# metadata read used for version checks. There is no delete and no policy, auth or mount administration here.

path "modulus-prod/data/tenant-db/*" {
  capabilities = ["create", "update", "read"]
}

path "modulus-prod/metadata/tenant-db/*" {
  capabilities = ["read"]
}
