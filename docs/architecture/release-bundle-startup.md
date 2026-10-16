# Assembled release payload startup

ARCH-11-FR-082 requires the downloadable payload to run without application
source, Node.js or the .NET SDK. Checksums and a nonempty archive alone do not
prove that its Compose, migrations, role provisioning and runtime instructions
work together. ARCH-11-FR-052 also requires API/Worker shutdown and restart proof.

## Assembled acceptance-record gate — 2026-10-15

The mandatory Production bundle startup driver now executes the bundled
`record-release.py` after its existing migration, image identity, database
outage, graceful restart and password-rotation checks. The phase requires its
own `strataai-bundle-smoke-<random>` project before any mutation. A private
acceptance directory sits outside the immutable payload and is removed by the
maintained EXIT cleanup, including its deliberately persistent lock file.

The phase initializes an accepted reference, checks its complete build/image
identity and input digests against the selected bundle, then requires repeated
acceptance to preserve the record bytes. It gracefully stops only its own
Worker, requires a clean exit without OOM, and requires the real command to
refuse acceptance with its expected sanitized diagnostic and unchanged record.
Restart must reuse the same Worker container/image and preserve PostgreSQL,
API and web container/image/start identities. The final successful check must
still preserve the original accepted record exactly.

The first native setup supplied canonical CI metadata as served web metadata;
initial verification correctly refused that fixture shape and cleanup passed.
A later native diagnostic run exposed an integration issue in the new phase:
Docker reassigns the ephemeral host port when the same Worker restarts. The
new port serves readiness 200 and the correct runtime identity, while final
verification with the old address fails. The corrected phase reuses the
existing strict loopback address discovery before the final command. A prior
rotation unit fixture also needed its extraction boundary moved to the new
phase call; its original rotation assertions are retained.

The corrected complete phase passes in **59.396 seconds** with fresh PostgreSQL
17.11, all 149 migrations and canonical restricted roles, actual current
compiled API/Worker and a separate Nginx runtime. It observes the changed Worker
diagnostic address, readiness 200 and matching runtime identity, a real same-
container/image restart, unchanged API process and removed record scratch.
All **718 captured source/database/driver inputs** and **71 API/Worker binary
files** match afterward. Owned containers, database volume, network and
**23,899 temporary bytes** are removed; original Docker inventory is restored.
All unsuccessful private attempts also clean up their owned resources.

Six driver tests pass **26 script scenarios** plus registration assertions:
namespace refusal before mutation, initial/repeated/final record failure,
incorrect project/image/extra fields, unavailable-Worker false acceptance,
unrelated errors, record mutation, stop/exit/OOM/recovery failures, replaced
containers/images, changed unrelated host processes and failed address refresh.
Those command-substitute scenarios verify orchestration, not live services;
the independent native phase executes the real record/runtime commands. The
existing cleanup tests now also require acceptance-directory removal.

All **79 startup cases and 285 CI guard cases** pass after the address correction
and fixture-boundary repair, **364 total**. The full solution builds with zero
warnings/errors; Bash/workflow lint and documentation navigation checks pass.

The native fixture mounts current compiled `b51da233` API/Worker assemblies in
retained runtimes and supplies matching web/CI metadata. It does not execute the
entire newly assembled downloadable bundle or certify current immutable images,
real two-version application rollback, green release CI or managed providers.
Those remain acceptance requirements. This phase initializes a first reference
with no prior release; non-null history and rollback-record behavior retain the
separate [state/CLI evidence](../release/promotion-and-rollback.md#atomic-acceptance-record-verification--2026-10-15).
No application image is built/pulled/imported, existing local service/database
used, external provider called or large-load test run.

## Independent 5845da8b bundle rehearsal — 2026-10-15

The original [release artifact 12002794848](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38713394901/artifacts/12002794848)
for `5845da8be8c5a4b476e0743c0b3a31fc01d6e46f`, version **0.1.0-1770**,
passes an independent source-free rehearsal. Its **518,743,026-byte** ZIP matches
published SHA-256
`c017f164865a9b87ac00354d0aa136a0bdd4e3564a89341b336a55692fe1de95`.
The candidate's pinned verifier admits all **189** files, nested image/security
checksums, canonical build metadata, image provenance and SBOM inventories.
The three original image archives are loaded and their actual image IDs and
OCI identities verified. No application source is built or mounted.

| Rehearsal | Verified behavior | Result and duration |
| --- | --- | --- |
| Production | Migrations/restricted roles, private diagnostic ports, health/proxy/build/mode, database outage and unchanged-process/volume recovery, graceful restart and runtime-password rotation | Pass, 212.331 seconds |
| Demo | Exactly three retained images, no database/volumes, health/build identity, SPA/proxy, documented seeded login and authenticated profile | Pass, 27.646 seconds |
| HTTPS | Only TLS published, missing/mismatched key refusal, trust/name/HSTS/Secure cookies, certificate renewal/restoration, persisted task and web restart, database/key recovery, protected Worker/WSS delivery and logout | Pass, 215.548 seconds |

The HTTPS recovery uses a distinct PostgreSQL cluster and independently
recovered encrypted data-protection keys. The original saved session and fresh
Secure sign-in both recover the exact task graph. Source-issued activity
continuation, exact restored page and Card rebinding refusal pass. The restored
Worker delivers an edit through the protected Board stream; anonymous/origin
refusals, content-free frames, persisted-cursor recovery and logout withdrawal
remain required and pass. The original source deployment and all image identities
remain unchanged.

Current verification drivers are frozen at parent `8940226b`; their private
orchestration uses the diagnostic port overlay and external read-only runtime
and certificate-lifetime probes. The runtime probe has no source difference
from the candidate's original command. The later certificate probe and recovery
verification tools are outside the payload. This exercises original application
images and operator commands with newer checks; it does not claim the original
CI contained every later gate. All original payload bytes are checked unchanged
after every stage and the pinned verifier is rerun at the end. Frozen source
input hashes have zero mismatches.

The complete check takes **499.581 seconds**, including download, integrity
verification and cleanup. All owned containers/volumes/networks and the three
newly imported images are removed. The original Docker inventory is restored,
and **1,045,044,568 temporary bytes**, including downloaded/extracted archives
and private fixture files, are removed. The original database and running local
Demo are preserved. No large-load or capacity test runs.

This is evidence for this exact candidate, supplementing its all-16-jobs-green
hosted run. It predates later main changes, including workload-identity and atomic
certificate-file replacement repairs. Passing certificate rotation in this
invocation does not waive that known repair. Current-main immutable CI, managed
storage/mail configuration, operational alerts, scheduled/coordinated recovery
and complete [MVP acceptance](operational-mvp-milestone.md#release-acceptance)
remain required. ARCH-10/11 and the milestone stay open. Follow the
[candidate's pinned operator guide](https://github.com/nickmailhot-lang/StrataAI2/blob/5845da8be8c5a4b476e0743c0b3a31fc01d6e46f/docs/release/README.md).

## Private diagnostic ports during verification — 2026-10-15

Independent verification of the original `e5570793` release exposes a collision
with the running local Demo API's port 8080. A successful host socket preflight
does not establish that Docker's port allocator can publish that port. The
failed invocation removes its owned project, newly imported images and
**1,045,017,209** temporary bytes; the original Docker inventory is restored.
The existing Demo stays running.

The maintained Production startup gate now creates an owned, temporary Compose
overlay outside the release payload. It replaces only diagnostic publications
with loopback ports allocated by Docker. The API/Worker/web internal ports,
images, environment, health, migrations, roles and durable storage still come
from the original bundle. The operator payload retains its published port
configuration and its original bytes.

Before each runtime check, the gate discovers its own project's three bindings
and exports the corresponding health/build verification URLs. Each binding must
be a single canonical `127.0.0.1` address with a port from 1–65,535. This also
refreshes addresses after password rotation recreates the API/Worker. Existing
readiness, exact identity, graceful shutdown, same-container restart, database
outage/recovery, wrong-password refusal and unchanged-payload assertions retain
their original deadlines. Cleanup removes the port overlay alongside owned
settings and runtime captures.

All **356** selected startup, database-outage and CI coverage cases pass,
including nine address-admission cases and overlay removal on successful and
failed cleanup paths. Bash syntax, workflow lint and diff checks pass.

### Independent original-bundle check and refusal

The original `e5570793` bundle ZIP is independently downloaded and its exact
**518,744,746-byte** size and published SHA-256 verified. The candidate's pinned
verifier admits all **188** extracted files, nested checksums, build identity,
image/security provenance and four SBOM inventories. The three original image
archives are loaded and their actual image identities checked against those
records. Application source is neither built nor mounted.

With the new port overlay, the original Production hosts start and pass health,
proxy, embedded build/service identity and Production-mode checks alongside the
existing local Demo. The complete newer gate **does not pass**: after stopping
the owned PostgreSQL service, a request in the first API liveness/readiness pair
times out after **15 seconds**, with no response bytes. The stage exits **28**
after **161.272 seconds**. The captured log does not distinguish which of the
two URLs timed out. Demo and HTTPS stages are not executed after that refusal.
This is a measured historical-candidate limitation, not a passing recovery drill
or evidence that current main failed.

The bundle predates the `cb39f549` bounded-readiness repair. Current main's
[database readiness probe](../../src/StrataAI.Infrastructure/Runtime/ProductionRuntimeDependencyStatus.cs)
uses a two-second cancellation budget; current-image outage acceptance remains
required. Three current compiled-code cases pass for an unresponsive Npgsql
handshake, caller cancellation and an already-cancelled probe, with owned cleanup
restoring Docker inventory. These controlled peers are separate evidence from
the failed original-image outage rehearsal. The newer `e83a928a` completed CI candidate has no application-source
changes from `e5570793`, but has not been independently executed here. Its
hosted green result does not certify the later outage requirement.

For this historical check, the newer read-only runtime and certificate probes
are supplied as external verification tools, with path-only driver adaptations;
they are not inserted into the original payload. All refusal deadlines remain
unchanged. The failed fixture removes all newly imported images and owned
containers, volumes and networks, restores the original Docker inventory, and
removes **1,045,025,366** temporary file bytes. The original database and existing
Demo are preserved. No large-load or capacity test runs. See the
[MVP status](operational-mvp-milestone.md#current-status) before promotion.

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

## Runtime database secret rotation

ARCH-12-AC-003 now has a small rehearsal in the existing mandatory assembled
Production gate. After startup and same-container graceful restart, it replaces
the two disposable runtime-role passwords. The supplied operator provisioner
reads those settings from PostgreSQL's environment, so the gate refreshes that
container while requiring the same named database volume, then provisions the
roles from the payload's SQL. Both new credentials must read the unchanged
migration-ledger fingerprint over the Compose `postgres` network address used
by the application hosts. A first real probe through PostgreSQL's loopback
address failed the gate because that image's local trust rule accepted the
previous password; loopback authentication is not rotation evidence. The
protocol fixture now requires the application network address. Each old
password must produce an explicit
password-authentication refusal; a network/transport failure is not accepted
as proof of rotation.

The gate then recreates API and Worker with the new settings. Both must have
new container IDs while all three application image IDs and runtime revisions
remain the original verified values. The complete health/readiness/proxy check
must pass again. This uses no source build, extra image, application account,
provider delivery or capacity dataset. Existing scoped teardown and final
unchanged-payload verification still govern release upload.

Sixteen additional protocol scenarios exercise successful rotation and failures
in environment writing, PostgreSQL replacement/startup, volume identity, role
provisioning, either new password, either old password, network refusal, ledger
preservation, host replacement/startup and final readiness. All 36 startup
tests and all 331 selected CI guards pass. The
[operator procedure](../release/README.md#rotate-local-runtime-database-passwords)
documents the interruption and preserved images/project/volumes. Managed
database, administrator, provider and signing/encryption-key rotations remain
separate acceptance work.

The actual unmodified Bash driver passes locally with the original retained
`24f56246` image archives: all 149 migrations, restricted Production startup,
health/proxy/build and image identity, graceful same-container restart, then
both role-password rotations and new API/Worker containers with the same
application image IDs. The network-address probe proves both new logins and
both old-password refusals, while the named volume and ledger fingerprint
remain unchanged. All 157 captured driver/Compose/schema inputs match during
execution. Cleanup restores the original container/image/network/volume
inventory and removes 522,340,183 bytes of temporary payload copies; the
original database is unused. This is scoped operator-driver evidence using
earlier images, not a new full-release certification. Current exact-commit
immutable CI and the remaining ARCH-12 acceptance still govern closure.

## Runtime metadata status and request deadlines

The assembled Production check previously used `curl --fail` for web/API/Worker
build metadata. That accepts HTTP redirects. A real-HTTP fixture returning a
301 with otherwise matching revision/version is incorrectly accepted by the
previous check; the healthy 200 fixture passes. The old request also fails the
new stalled-response deadline guard.

Each metadata request now requires HTTP 200 before JSON identity validation,
with a five-second connection bound and 15-second total bound. Its response
uses a private temporary file removed by the existing teardown, including when
Docker cleanup or resource inspection fails. No redirect is followed and no
response body is logged. Health checks and the original image/revision/version
comparisons remain mandatory before and after restart and secret rotation.

Seventeen real-HTTP scenarios cover matching identities, 301/302/307 at each
of the three endpoints, each endpoint unavailable, wrong revision/version,
malformed JSON and a stalled response. The stalled fixture requires curl's own
timeout result; an outer harness kill cannot count as a passing refusal. The
fixture server also bounds its socket lifetime to prevent an orphaned Windows
curl from holding the test child's pipes. All 53 startup scenarios and all 348
selected CI guards pass, with Bash syntax, workflow lint and the mandatory
integration inventory also passing.

The actual updated Bash driver passes locally with the archive-bound
`24f56246` application images: all 149 migrations, restricted Production
startup, strict runtime metadata, health/proxy, original image identities,
graceful restart and both runtime password rotations. All 157 captured
driver/Compose/schema inputs match. Docker inventory is restored and
522,340,182 bytes of temporary payload copies are removed. This supporting
execution uses earlier tested images; current exact-commit complete CI and all
remaining release acceptance remain required.

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

## Database outage and bounded readiness (2026-10-15)

The source-free Production gate now stops only its disposable PostgreSQL
container and verifies API/Worker liveness remains HTTP 200 while readiness
returns HTTP 503. API refusal is restricted to its fixed status/code/message
and correlation reference; Worker refusal contains only `status`. Database
configuration, provider diagnostics and tenant content cannot satisfy those
response-shape checks. The operator's build-bound deployment check must refuse
the outage, using that same already tested command rather than a substitute.

The gate restarts the same PostgreSQL container without recreation and requires
the same named volume and migration-ledger fingerprint. The same API, Worker and
web containers must recover with unchanged start timestamps, followed by full
health/proxy/build checks and successful operator verification. The existing
graceful application restart and secret-rotation checks still run afterward.
This checks reconnection and schema persistence; it is not a backup restore or
proof of all task/provider effects during an outage.

The unchanged retained `24f56246` API times out at the original 15-second
readiness request limit after database shutdown. An initial cancellation-only
repair also fails the new stalled-handshake regression's six-second outer
deadline. Production readiness now uses its own data source with two-second
connection/command limits and a linked two-second cancellation budget. Its
pool settings are minimum zero/maximum one when pooling is enabled. Normal
application pool/command settings and the TLS/credential configuration remain
unchanged. Both paths share the same role and complete schema checks, and both
data sources are disposed. Caller cancellation remains cancellation; a probe
deadline returns not ready.

All three focused Linux runtime regressions pass in **4.455 seconds**, including
stalled PostgreSQL handshake, caller cancellation and pre-cancellation. Both
listener admission and result waits are bounded. The warning-as-error build
passes with zero warnings/errors. All **22** outage-gate refusal cases,
**53** existing startup cases and **244** CI coverage guards pass, including
unexpected healthy/failed liveness, private readiness fields, wrong operator
failure, container/process/volume/ledger replacement and restart/wait failures.

The maintained real stop/recover contract passes in **34.668 seconds** with
all **163 captured inputs matching**. Cleanup restores the original Docker
inventory and removes **2,111,069** temporary bytes; no original database,
application image build/import/download or large load is used. This supporting
run mounts the current compiled Infrastructure library read-only into verified
retained `24f56246` API/Worker images, with declared current Nginx/header mounts.
The library uses compatible `0.1.0-1736` assembly versions; an initial mixed-version
host fixture failed before startup. It does not certify new immutable images or
the complete release gate. Full current-image CI, deployment acceptance and
the broader ARCH-08/11 requirements remain open.
