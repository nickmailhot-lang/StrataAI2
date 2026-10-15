# Promote and roll back an immutable release

Use this with the [release guide](README.md), [configuration reference](../architecture/configuration.md), [schema compatibility rules](../architecture/schema-upgrades.md) and [operational MVP milestone](../architecture/operational-mvp-milestone.md). This is an operator procedure; its presence does not establish successful production promotion or rollback.

The [encrypted database recovery contract](../architecture/database-recovery.md)
adds a small isolated CI restore rehearsal. Scheduled deployment backups,
coordinated object-storage/key-ring recovery and measured production RPO/RTO
remain required; the CI fixture does not establish those operating controls.

## Preserve the deployment identity

Before the first deployment, choose a unique `COMPOSE_PROJECT_NAME` for each environment, such as `strataai-production` or `strataai-staging`. The example config supplies a stable production name. For an existing installation, use its existing project name, not a new example value. Keep that name in the operator's environment file across all release directories and commands. An explicit Compose project option must match it.

Compose owns the PostgreSQL and activity Data Protection key-ring volumes under that project. Changing the project name can create different volumes and an empty deployment instead of upgrading the existing one. Do not use a release version or extraction-directory name as the project identity. Do not remove those volumes during promotion or rollback.

The runtime environment file remains private and outside source control and uploaded artifacts. Retain durable keys and provider configuration independently of image bundles; a release bundle is not a backup of database, key-ring or object-storage data.

## Record the current known-good release

Before changing images, retain a non-secret deployment record in the operator's release inventory:

| Field | Evidence to retain |
| --- | --- |
| Environment and project identity | Current environment name and exact Compose project name |
| Current known-good revision | Full commit SHA, completed successful CI run URL, and retained bundle location |
| Image identity | Web/API/Worker references and image IDs from the verified original image metadata/provenance |
| Integrity | Bundle, image and security checksum manifests and original build metadata |
| Database compatibility | Applied migration versions, reviewed compatibility with both current and candidate images, and phased procedure for incompatible changes |
| Recovery preparation | Protected backup/restore evidence and locations for database, durable keys and object storage; no credentials in this record |
| Deployment result | Time, health/readiness/build-identity results and core workflow checks |

Keep the prior known-good record and bundle until the candidate is accepted and the deployment's retention policy permits removal. A candidate with unfinished or failed CI does not replace that record. The first deployment has no prior application release; record that explicitly and prepare a recovery procedure before exposing traffic.

## Promote the candidate

1. Require completed successful CI for the exact candidate revision, including required integration/security/release gates. Verify the retained image/security identities and checksums using the release guide. Load those original three images; do not rebuild them on the deployment host.
   Release Compose requires all three image references and never automatically
   pulls application images. An explicitly verified registry digest can also be
   fetched before starting the deployment; omitted or empty references fail
   configuration instead of selecting a development image.
2. Review migrations and application compatibility before applying changes. Forward-only or irreversible changes need a phased procedure and verified recovery plan. Do not assume an older image accepts a newer schema or reverse SQL files automatically.
3. Preserve the same project name and environment configuration. Set the three image references to the verified candidate values. Apply the candidate's migrations and provision restricted roles from its release directory, as documented in the release guide.
4. Start the candidate and run `bash verify-release-runtime.sh build-metadata.json` from its verified bundle. It requires health/readiness for web, API, Worker and the web-to-API proxy, then matches all three served build identities to that bundle. Configure `STRATAAI_WEB_URL`, `STRATAAI_API_URL` and `STRATAAI_WORKER_URL` for this same deployment when the loopback diagnostic defaults do not apply. The HTTPS-only overlay keeps API/Worker private; use approved private diagnostic addresses rather than opening their ports. Validate sign-in, a persisted Board/List/Card workflow, authorized collaboration and enabled storage/mail providers through the deployment's configured origins.
5. If any required check fails, mark promotion failed and keep the prior known-good record available. Route traffic only when required checks pass. Record the candidate as known-good only after its measured exit criteria are satisfied.

## Roll back a failed promotion

Use the retained previous bundle and its verified image references, the same private runtime settings, project name and durable volumes. Redeploy those original images without recompilation, then repeat health/readiness, build-identity and core workflow checks against the previous revision. Record the failed candidate and rollback result separately; do not overwrite the successful previous-release evidence with a failed attempt.

Image rollback is permitted only when the previous image is compatible with the database and current provider contracts. When it is not, follow the reviewed phased recovery procedure: a tested forward repair or explicitly planned restoration, including writes and external effects since the backup. Restoring PostgreSQL alone does not restore object storage, key rings or external deliveries. This guide does not supply automatic database reversal or claim a completed backup/restore drill.

## Current evidence and limits

### Production runtime admission (2026-10-15)

Matching image revisions alone cannot establish Production configuration: the
same retained images intentionally support Demo mode. The former operator
command accepts all **14 new negative HTTP cases** for Demo/missing/null/unknown
or incorrectly cased runtime mode and missing/wrong service identity. Each
response is healthy and carries the expected build revision/version, so these
failures establish the missing deployment check rather than a build mismatch.

`verify-release-runtime.sh` now requires API `service: strataai-api` and Worker
`service: strataai-worker`, each with exactly `mode: production`. Web metadata
retains its build-only contract. The command remains a read-only Production
acceptance check: no new mode switch, deployment, migration, release-record
rewrite or rollback is performed. Existing status, build identity, byte limits,
deadlines, TLS validation, private diagnostics and cleanup rules remain intact.
The database-outage HTTP fixture now includes the real application runtime
service/mode fields rather than an incomplete synthetic response.

All **382 HTTP/startup/outage/CI coverage checks** pass, including the **45**
operator-runtime cases and the 14 before/after negative regressions. Bash syntax,
diff checks and the 20 local documentation links pass. Full immutable-image CI
and the remaining ARCH-12 requirements still govern acceptance; large-load tests
remain disabled until after the operational MVP milestone.

The separate real retained-image check executes **11 operator outcomes**. It
passes Production, rejects the wrong selected revision and a stopped Worker,
then passes that restarted Worker. With the same verified image revisions and
healthy responses, the old command accepts each misconfiguration: Demo API
with Production Worker, Production API with Demo Worker, and both Demo. The
revised command rejects all three and passes after restoring both to Production.
Each phase checks actual runtime mode/service/build and unchanged container
identities during verification; deliberate mode changes recreate only owned
fixture hosts. Original image IDs and the private known-good record are preserved.

All 158 captured runtime inputs match. Fresh PostgreSQL/pgvector uses all 149
migrations and restricted roles; current Nginx configuration is mounted read-only
over archive-verified `24f5624` images. API verification is optional and mail
disabled for the Production fixture; the Worker retains its verified-account
policy. Full Docker inventory is restored, owned credentials/resources removed
and **32,629 temporary bytes** reclaimed. No images are built, downloaded or
imported, and the existing database is unused. All **37 release-artifact checks**
also pass. This supporting fixture does not certify current-main images, actual
deployed providers or two-version application rollback. ARCH-12 remains open.

### Build-bound operator verification (2026-10-15)

The bundled `verify-release-runtime.sh` supplies the health and served-build
check used before accepting a Production release. It requires the selected,
already verified `build-metadata.json`; invalid or ambiguous expected metadata
fails before HTTP. Six health/readiness/proxy responses and all three build
responses must be HTTP 200 without redirects. Every served revision/version
must equal the selected bundle. API and Worker must additionally report their
respective `strataai-api` / `strataai-worker` service identities and `production`
mode. Healthy Demo or mixed-mode hosts cannot satisfy this Production check.
Responses and diagnostic configuration remain
private; the command prints neither response bodies nor configured URLs.

Metadata reads store at most 4,097 bytes and reject bodies above 4,096 bytes,
including chunked responses with no declared size. A failed/truncated transfer,
multiple JSON documents, missing identity or mismatch fails. Each metadata
transfer has at most 15 seconds (or the smaller configured health timeout).
The original six health requests retain their configured 1–300 second limits.
Both commands ignore personal curl configuration, restrict protocols to HTTP(S)
and connect directly; they cannot inherit a local redirect or insecure-TLS rule.
Normal TLS trust/hostname validation remains enabled.

This is a read-only check. It does not deploy images, apply migrations, replace
the known-good inventory, query GitHub for green CI or validate image archives.
Those earlier promotion steps remain required. A nonzero exit fails this
promotion step; preserve the prior known-good references and follow the
reviewed compatibility/rollback procedure. Older bundles may lack this command:
use the guide and tools included with the selected release.

All 31 new real-HTTP verification cases, 18 existing health cases, 53 startup
checks, 243 CI coverage guards and 37 release-artifact tests pass. They include wrong identities
at each host, malformed/multiple/oversized/chunked/truncated/stalled metadata,
redirects despite local curl configuration, unavailable health, invalid expected
metadata and cleanup without changing a prior-reference fixture. Artifact tests
refuse a missing command even after checksums are regenerated.

A separate real retained-image run passes the correct release, refuses a
different selected revision while hosts are healthy, refuses a stopped Worker,
then passes after restarting that same Worker container. All application
container/image identities and a private known-good reference record remain
unchanged. All 158 captured runtime inputs match; owned cleanup restores the
original Docker inventory and removes 29,958 temporary bytes. This uses verified
`24f56246` images, current declared Nginx/header mounts, fresh PostgreSQL with
149 migrations and restricted roles; it does not certify new candidate images
or a two-version application rollback. No application build/import/download,
original database use or large-load testing occurs. Full current immutable CI,
managed-provider operation and broader ARCH-12 acceptance remain required.

The release image-selection checks execute Docker Compose configuration with
each application reference omitted and explicitly empty. Before repair all six
negative cases accepted a `:local` fallback, while both explicit tag/digest
cases lacked a no-pull policy. Release Compose now requires every reference
and uses `pull_policy: never` for web/API/Worker. All 17 configuration tests
pass, including the four image-selection tests and their per-host subcases;
the 27 artifact-integrity tests, mandatory integration inventory and workflow
lint also pass. These are configuration and integrity checks: no container,
image, volume or database is changed. New exact-commit immutable-image CI is
still required, and this does not establish production promotion or rollback.

The release health script requires HTTP 200 from all six health/readiness
endpoints, rejects redirects, discards response bodies and bounds each request.
Six real-HTTP redirect cases fail against the former script because it reports
success. The repaired gate passes all 18 real-HTTP fixture checks, including all
six redirects, all six unavailable endpoints, healthy discovery, a stalled
response and four invalid timeout settings. The test fixture also has an outer
deadline so a missing request timeout cannot leave CI hanging. These fixtures
verify the deployment check's refusal behavior, not the candidate application's
actual health. The two startup refusal guards and 21 release-artifact integrity
checks also pass; workflow lint passes. Full current CI is still required.

The example configuration was rendered from two differently named release directories: both select the same project, PostgreSQL volume and activity-key volume. A distinct staging project selects separate volume identities. These checks are read-only Compose configuration checks; no live application or existing volume was changed. Current exact-candidate CI, retained-image promotion/rollback rehearsal, backup restoration and managed-provider validation remain pending. ARCH-12 stays open.
