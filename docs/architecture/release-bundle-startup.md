# Assembled release payload startup

ARCH-11-FR-082 requires the downloadable payload to run without application
source, Node.js or the .NET SDK. Checksums and a nonempty archive alone do not
prove that its Compose, migrations, role provisioning and runtime instructions
work together. ARCH-11-FR-052 also requires API/Worker shutdown and restart proof.

## Source-free Demo release path

ARCH-10-AC-001 also requires Demo to start from the image archives on a clean
Docker host. The source-build `compose.demo.yml` was absent from the release
payload, whose existing startup gate exercises Production. The bundle now
includes the image-only `compose.demo.release.yml` and redacted
`.env.demo.example`, with a [Demo quickstart](../release/README.md#demo-from-tested-images)
and the known seeded account. Demo starts web, API and Worker without a database,
application source or a host-side build.

The additional mandatory
[Demo payload gate](../../scripts/ci/test-release-demo-bundle-startup.sh)
starts exactly those retained images, checks health/readiness, build identity,
SPA/proxy delivery and actual seeded login plus authenticated profile access.
It rejects existing operator settings/source trees and fails when startup,
identity, health, authentication or cleanup cannot be verified. Both Production
and Demo checks run before the final unchanged-payload verification and upload.
The release integrity verifier requires both new Demo assets and still rejects
real `.env.demo` settings from uploaded artifacts.

All 20 Demo driver self-test scenarios pass, covering successful startup and
negative paths for redirects, wrong images/mode/account, failed login, unexpected
volumes, cleanup failure, inventory failure and leftovers. Actual local Compose
checks verify image-only settings and missing-image refusals. A live local
source-free run passes with the already-loaded, archive-verified `24f56246`
images: three services, no database or volumes, health/build identity, SPA/proxy,
seeded login and authenticated profile access. The application containers have
no installed .NET SDK. The new configuration places API/Worker on an internal
network and gives only web a separate entry network with a loopback port;
publishing ports directly on the fully internal network failed in the initial
Docker Desktop invocation. The corrected run restores the original container,
image, network and volume inventory and downloads/imports no images.

That initial local run uses Python orchestration. A subsequent execution of the
actual Bash driver with real Docker/curl/jq reaches sign-in but returns 400:
the generated retry key has 32 hex characters, while the API requires a
36-character UUID in hyphenated format. The script now formats the random key
accordingly; the API contract is unchanged. The healthy protocol fixture now
enforces that external header contract and fails against the previous script.
After repair all 270 selected CI guards, including the 20 Demo-driver scenarios,
pass. The actual Bash driver also passes health/build/mode, SPA/proxy, seeded
sign-in and authenticated profile checks against the archive-bound `24f56246`
images, then restores the original Docker inventory. No image is downloaded or
imported and no original database or host SDK/source build is used.

These are scoped configuration and driver results using the earlier images,
not release certification for a new SHA. New exact-commit assembled-release CI,
including both startup modes and unchanged-payload verification, remains required.

After the complete release input/bundle checks and before artifact upload, CI
runs `scripts/ci/test-release-bundle-startup.sh bundle`. The check enters the
assembled folder and refuses application source folders, `.git` or an existing
operator `.env`. It uses only that payload's archives, metadata, Compose file,
migrations and supplied runtime scripts. It does not compile or rebuild images,
mount application source or invoke Node/.NET SDK tools. Bash, Docker/Compose,
curl, jq, openssl and Python's provenance verifier are CI orchestration tools; the ordinary operator commands
retain their documented requirements.

The check creates a uniquely named Compose project and disposable secrets, masked
on GitHub Actions. It loads each archive and verifies the retained image identity,
including archive-proven config/manifest ID conversion described below, then starts
a fresh PostgreSQL/pgvector volume, applies all ordered migrations with the
payload's advisory-lock runner, and provisions separate restricted API/Worker
roles. The actual Production hosts must pass the supplied health script, including
readiness and the web-to-API proxy. Web/API/Worker runtime revision/version and
running image IDs must match the verified local IDs bound to the payload.

API and Worker are stopped with a 20-second bound. Both must exit 0 without OOM,
then [Compose start](https://docs.docker.com/reference/cli/docker/compose/start/)
must restart the same container IDs and pass all health/identity checks again.
Cleanup removes only the owned Compose project, including its fresh volumes and
network, and its generated `.env`. Surviving owned containers/volumes or a failed
cleanup make the check fail. The original bundle verifier runs again after the
smoke, before upload, requiring unchanged original metadata/provenance/checksums
and refusing any leftover private file. No failure can still reach release upload.

Fifteen workflow mutations protect the startup gate, the post-startup validation
gate and mandatory CLI refusal regressions against omission, replacement with
success, skipping, ignored failure and invalid ordering. Two actual Bash CLI tests
require existing operator settings to survive and application checkouts to be
refused before secret creation or Docker/SDK commands. The tests remove external
commands from PATH to prove early refusal. They exposed an initial combined shell
guard that continued past an existing `.env`; separate mandatory checks fix it.
Combined workflow/metadata/CLI tests pass **181/181**; release validation remains
**21/21**. The required integration matrix
remains four groups, seven executions and 112 registered checks.

## Local source-free payload evidence, 2026-10-09

The complete helper passes on Docker Desktop using the three exact exported and
reloaded archives from `1176d4929ac5c44471f48ec1ac3034edd47f6b87` and the committed
operator scripts/Compose/migrations. The private payload contains no `.git`,
`src` or `apps` folder. Its metadata is explicitly local; no GitHub workflow
identity, successful security scan or SBOM is fabricated. This is operator-payload
startup evidence, not a full CI release bundle validation.

Fresh database migration and restricted Production startup, all six health checks,
all three runtime/image identities and graceful API/Worker stop/start pass; the
process exits 0. Independent verification finds zero owned containers, volumes
and networks, and no generated `.env`. All three archive hashes are unchanged.
The three pre-existing running services remain intact.
The final credential-cleanup implementation also passes the complete startup and
restart invocation. Cleanup is installed before any secret content is written,
so normal script failure during a write invokes credential removal. Abrupt host
or process termination can bypass shell traps; crash-safe erasure is not proven.

The host already had Docker and cached dependencies. This does not establish a
physically clean host, cold-start timing, deployed object storage/mail/scanner
settings, optional metrics/attachment overlays, production job durability under
restart, or successful current full GitHub CI. The CI check still needs execution
on its gated candidate and retained artifacts. ARCH-11 remains open.

## Image identity across Docker stores (2026-10-14)

Docker supports [classic and containerd image stores](https://docs.docker.com/engine/storage/containerd/).
When the retained `7b155f49` archives were loaded locally, CI's three configuration
IDs differed from Desktop's three manifest IDs. Each original archive manifest
hashed to the loaded descriptor and referred to the exact configuration digest
recorded in CI. The old literal-ID comparison would reject this unchanged image.
[OCI manifests describe configuration by digest](https://github.com/opencontainers/image-spec/blob/main/manifest.md).

The provenance CLI retains strict ID equality by default. With `--expected` and
`--archives`, a differing ID requires the original saved archive's single image
manifest and configuration: hash-checked metadata blobs, canonical descriptor
types/sizes, matching immutable labels, and the loaded descriptor for a manifest
ID. The retained and loaded IDs must both belong to that same digest pair.
Missing/foreign/corrupt archives, changed content under a digest name, ambiguous
metadata and a substituted loaded descriptor are refused. It reads bounded
metadata without extracting layers or copying runtime/symbol trees. Original
provenance/schema, checksums, build identity, security evidence and image labels
remain unchanged; labels alone cannot authorize conversion.

Integration and security load gates require this proof against their retained
archives. The source-free startup driver invokes the same verifier before
starting services, then fences each running container against its verified local
image ID through shutdown/restart. Its private inspection file is removed by
cleanup. Python is a CI verification tool; the supplied operator startup commands
retain their requirements and do not need an application checkout or SDK.

All **18 provenance tests**, **25 release-artifact tests**, **224 CI/shard/startup
guards**, actionlint and Bash syntax checks pass. A real Desktop probe loads
three explicitly synthetic scratch-image archives totaling **2,424 bytes**:
unproved conversion is refused, archive-bound conversion preserves the original
record, and three created-but-unstarted containers report those verified local
IDs. Owned containers, images and archives are removed. These tiny identity
fixtures do not establish production startup, graceful process restart, cold
capacity or a current full release. The existing full bundle startup/restart and
post-startup integrity gates remain mandatory in current CI. ARCH-11 stays open;
large-load testing stays disabled until after the operational MVP milestone.

## Retained-image source-free startup follow-up — 2026-10-14

The unmodified maintained startup driver at `436ebe12` passes against the three
original retained images from `17bc37d7` / CI run `38565885906`. Application,
schema and image-build source differences between those images and the runtime
script candidate are empty. Original image archive checksums and metadata are
verified before loading. The supplied payload contains deployment scripts,
configuration examples and migrations, without `.git`, application source,
Node.js or a .NET SDK.

The driver exits zero after migrations, restricted API/Worker role provisioning,
Production health/proxy/build identity and graceful API/Worker shutdown and
restart using the same verified image IDs. All 161 captured runtime/schema
inputs match the worktree, and original archive checksums remain unchanged.
The fixture keeps large-load testing disabled and does not certify enabled
mail/object-storage providers or a current full release bundle.

The outer local audit exits one at its final machine-wide network-ID inventory
comparison, after the maintained driver has passed and cleanup has run. This
is retained as an audit failure, not a wholly green invocation; its cause is
not established by the capture. Independent checks confirm zero containers,
volumes or networks in the exact owned Compose namespace, removal of all three
newly loaded application images and the downloaded ZIP/image copies, and
removal of temporary credentials/inspection files. Docker events show creation,
use and destruction of the owned network. Container and volume inventory
comparisons pass; the original stopped database remains preserved.

Evidence is retained privately under `mvp-bundle-9d96e413` (startup console,
metadata/checksums, 161-input manifest, maintained-driver outcome and independent
cleanup verification). Current full immutable-image CI, release assembly and
post-startup integrity remain mandatory. ARCH-11 and the operational MVP
milestone remain open.

## Fail-closed owned cleanup — 2026-10-14

The maintained startup gate now queries the exact disposable Compose namespace
for containers, volumes **and networks** after teardown. Previously networks
were omitted, and a failed Docker inventory command could produce an empty
string that appeared clean. A leftover resource or a failed query now fails the
gate. Each inventory is checked even after another fails. The existing teardown,
private environment/inspection removal and preservation of an earlier startup
failure remain in place. No pre-existing resource is deleted by these checks.

Nine added executable EXIT-handler scenarios cover clean completion, an earlier
startup failure, each of the three leftover resource types, each of the three
inventory failures and Compose teardown failure. Docker is replaced by a test
stand-in, and each case verifies cleanup of private temporary files and exact
project-scoped queries. All **11 startup tests**, **233 CI/startup guards** and
Bash syntax checks pass. This change closes an owned-cleanup observation gap;
it does not explain the earlier machine-wide network inventory discrepancy or
turn that outer audit into a pass. Current complete immutable-image release CI
and provider acceptance remain required; ARCH-11 stays open.

## Compose v2 restart compatibility — 2026-10-14

Retained `17bc37d7` run `38565885906` passed every source, security and runtime
integration group and its required aggregate, but release job `115952894805`
failed after healthy startup and graceful API/Worker stop: its Compose runner
rejected `start --wait` with `unknown flag: --wait`. No completed release bundle
is claimed for that run.

The driver now restarts only API/Worker with
`up --no-deps --no-recreate --wait --wait-timeout 120`. The same runner already
supports waiting on `up` for initial startup. The
[Compose up contract](https://docs.docker.com/reference/cli/docker/compose/up/)
defines `--no-recreate` to preserve existing containers and `--wait` to require
running/healthy services. Existing graceful exit/OOM checks, original container
IDs, verified image IDs, post-restart health/build identity and deadlines remain
mandatory. There is no fallback that ignores readiness or recreates a failed host.

An executable regression rejects the original unsupported command before the
repair. Nine restart scenarios then pass for portable restart, API/Worker exit
failures, API/Worker OOM, each replacement container, failed readiness waiting
and failed post-restart runtime verification. All **20 startup tests**, **242
CI/startup guards** and Bash syntax checks pass.

The complete changed worktree driver also exits zero against original once-built
`3f9957d2` images: migration/provisioning, Production startup, health/proxy/build
identity, graceful stop, same-container/image restart and scoped cleanup pass.
The capture explicitly records a dirty worktree and driver SHA-256
`168b2d26a299e3b627fd027956e5e8ad2cb1cff23c5f7d30fe06c95353b34b7c`;
this is supporting repair evidence, not a fully green new-commit release.
All 161 captured inputs match and original image archive hashes are unchanged.
Private evidence is under `mvp-bundle-c00b45b3`.

The outer whole-network inventory audit still exits one because only the
built-in `bridge` ID changes; its replacement cause is unestablished. Independent
checks preserve all original containers/volumes and every other network ID, find
zero owned resources and confirm removal of three loaded images, ZIP/tar copies
and temporary credentials/inspection. Current full immutable CI and actual
release assembly/post-startup integrity remain required. ARCH-11 and the MVP
milestone stay open; large-load tests remain disabled.
