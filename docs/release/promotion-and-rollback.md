# Promote and roll back an immutable release

Use this with the [release guide](README.md), [configuration reference](../architecture/configuration.md), [schema compatibility rules](../architecture/schema-upgrades.md) and [operational MVP milestone](../architecture/operational-mvp-milestone.md). This is an operator procedure; its presence does not establish successful production promotion or rollback.

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
4. Start the candidate and require health/readiness for web, API, Worker and the web-to-API proxy. Check that API and Worker build metadata match the candidate revision. Validate sign-in, a persisted Board/List/Card workflow, authorized collaboration and enabled storage/mail providers through the deployment's configured origins.
5. If any required check fails, mark promotion failed and keep the prior known-good record available. Route traffic only when required checks pass. Record the candidate as known-good only after its measured exit criteria are satisfied.

## Roll back a failed promotion

Use the retained previous bundle and its verified image references, the same private runtime settings, project name and durable volumes. Redeploy those original images without recompilation, then repeat health/readiness, build-identity and core workflow checks against the previous revision. Record the failed candidate and rollback result separately; do not overwrite the successful previous-release evidence with a failed attempt.

Image rollback is permitted only when the previous image is compatible with the database and current provider contracts. When it is not, follow the reviewed phased recovery procedure: a tested forward repair or explicitly planned restoration, including writes and external effects since the backup. Restoring PostgreSQL alone does not restore object storage, key rings or external deliveries. This guide does not supply automatic database reversal or claim a completed backup/restore drill.

## Current evidence and limits

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
