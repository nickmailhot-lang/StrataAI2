# Promote and roll back an immutable release

## Nested release-record refusal — 2026-10-16

Candidate metadata, image provenance and saved deployment state have separate
4,096/16,384/32,768-byte bounds. A malformed nested record can fit those bounds
and still exceed the JSON parser's recursion limit. Python 3.12's default
accelerator exposes this for 6,001-byte provenance/state inputs; the standard
pure-Python fallback also exposes it for 2,401-byte candidate metadata. The old
recorder emits tracebacks instead of its fixed refusal. Baseline tests reproduce
five library errors and five CLI diagnostic failures across both parser paths.

Only parser recursion is now normalized to the existing record-shape refusal.
Strict schema/build/image validation, byte limits, lock handling, original OCI
archive binding, runtime checks and atomic record publication remain unchanged.
Malformed candidates preserve the last accepted state; malformed saved state
remains byte-for-byte available for operator inspection. Refusal performs no
runtime inspection and writes no acceptance marker or partial replacement.

All **41 recorder tests pass on Python 3.12.14 and 3.14**, including three new
methods covering all three inputs under both parser paths. Actual CLI checks
require exit 1, empty stdout and the fixed failure message; external tools are
absent from their PATH. All original 38 recorder methods remain unchanged.
These are small temporary-file/mocked-runtime checks, not a new native promotion
or rollback rehearsal. Current immutable-image/Linux CI, complete deployment
acceptance and managed-provider evidence remain required; ARCH-12 and the MVP
stay open, and large-load tests stay deferred.

## Archive-bound runtime identity and two-release rollback — 2026-10-16

Docker Desktop can expose the saved manifest ID where CI records the image's
config ID. The prior recorder rejects that legitimate representation change:
a fresh native Production fixture passes all three original archive bindings
and the real health/readiness/build/proxy verifier, but creates no acceptance
record. That baseline takes 97.323 seconds and restores Docker inventory.

The recorder now permits alternate IDs only through the existing original OCI
archive proof, including config/manifest digests, build labels and the loaded
manifest descriptor when applicable. Labels or tags alone cannot authorize a
change. Narrow image inspection reads only ID, labels and descriptor; no runtime
environment or full configuration is read. Records retain **original CI IDs**;
every replica's actual container/start identity remains checked before and after
runtime verification. Bindings are cached only within that invocation.

The checksummed bundle must include `verify-image-labels.py` beside the recorder.
Its archive-only path no longer imports the CI result-summary module; full CI
build validation still uses its canonical validator. Three positive subcases
fail before the ID repair, and the standalone packaging case separately exposes
the import dependency. After repair, all **38 recorder, 18 archive-identity,
47 release-payload and 289 CI coverage checks** pass, along with workflow lint.
Missing/corrupt/substituted archives, wrong labels and wrong descriptors refuse
acceptance without replacing the prior record. Reacceptance preserves its bytes.

The complete native rehearsal uses original bundles from green
[24f56246 / run 38603745814](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38603745814)
and [be7bf9e4 / run 38802201471](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38802201471),
with their GitHub artifact digests, schema/security/image evidence and all 149
identical migration files verified. These historical bundles predate newer
payload requirements; a standalone current operator toolkit checks their actual
immutable images, without declaring the old payloads current-format compliant.
One fresh database, restricted current runtime roles, one account and one Card
exercise five passing stages:

1. Accept the prior release and create the Card.
2. Deploy the candidate, stop PostgreSQL, observe readiness refusal and reject
   acceptance with the prior record byte-for-byte unchanged.
3. Recover the same database container/volume and redeploy prior images; the
   original session and Card remain usable without another sign-in.
4. Accept healthy candidate images, retain the prior reference and edit the Card.
5. Redeploy prior images and accept rollback, retaining the candidate's complete
   reference, its persisted edit/version, the session and migration ledger.

All stages pass in **240.798 seconds**, with **2,223 repository inputs unchanged**
and independently matching operator hashes. Owned containers/volumes/network,
three newly loaded images and **1,045,140,039 temporary bytes** are removed;
original Docker inventory is restored. Independent checks find no owned resources
or fixture directory. No application rebuild, mounted application assembly,
external provider or large-load test is used.

Earlier private setup failures remain recorded: raw-ID fixture comparison,
missing standalone import dependency, a client withholding Secure cookies on
local HTTP and an incorrect unchanged PostgreSQL start-clock assertion after
restart. The final client exception is loopback-only; production cookies remain
unchanged. Restart checks preserve container/image/volume/ledger identity while
requiring a new start instant. This historical-image/current-tool proof does not
certify current-main immutable CI, incompatible-schema rollback, managed
providers or deployment acceptance. ARCH-12 and the MVP remain open.

## Web proxy build identity — 2026-10-15

The operator verifier now also reads `/api/runtime` through `STRATAAI_WEB_URL`.
The web-facing API must match the selected revision/version, `strataai-api`
service and `production` mode. Successful direct API checks and proxy health
alone can conceal a web proxy routed to an older, otherwise healthy API.
The existing HTTP 200, redirect refusal, bounded transfer, timeout, JSON and
private-diagnostic requirements apply to this fourth metadata response too.

All 12 new negative proxy cases fail before repair: wrong/missing service,
invalid/missing/Demo modes, revision/version mismatch, redirect, unavailable
endpoint and malformed JSON. After repair all **75 real-HTTP runtime/health
cases**, **282 CI guards** and **47 release-artifact cases** pass. Mandatory CI
already executes the runtime tests and includes this command in the source-free
bundle and Production startup acceptance; no gate or assertion is removed.

A separate retained Nginx fixture routes `/api/runtime` to a different synthetic
API revision while direct API, Worker and web build metadata match and all six
health endpoints return 200. The old command accepts this mismatch. The repaired
command rejects it, then accepts the matching revision after Nginx reload, with
the same container/image/start identity and an unchanged known-good reference.
Both completed scoped runs preserve their three captured source inputs, remove
private scratch files and owned fixture directories/container, and restore the
original Docker inventory. No database, original local service, application
image build/download/import, external provider or load test is used.

The native result exercises real Nginx routing and the real operator command
with synthetic identity responses. It does not prove current application
deployment, successful two-version rollback or a passing current immutable-image
release. Those remain required before acceptance; ARCH-12 stays open and large
load tests remain deferred until after the operational MVP.

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

After completing the candidate's green-CI, original artifact/image verification,
migration review, deployment and core workflow checks below, use the bundled
Python 3.11-or-newer command for the final runtime check and acceptance record:

```bash
python3 record-release.py --bundle . \
  --state /var/lib/strataai-deployments/production.json \
  --environment production --project strataai-production
```

Use an existing protected state directory outside the release bundle and Git.
Choose the deployment's actual stable Compose project name; the example is not
a new project to introduce during an upgrade. The command needs Python 3.11 or newer and
the Bash/curl/jq/GNU tools already required by `verify-release-runtime.sh`, plus
the Docker CLI in the context of the selected deployment. That context must
permit read-only container listing and identity/state inspection. Run it in
that tool environment (Git Bash on Windows). Add `--initialize` only
for the first accepted deployment, which explicitly records no prior release.
Initialization refuses an existing record; normal acceptance refuses a missing
record or a different environment/project identity.

The command validates the selected build metadata and original image-provenance
shape/identity and checks the selected Compose project's actual running image
IDs before and after invoking the real runtime verifier with captured expected
metadata. All non-one-off web/API/Worker replicas must use their corresponding
provenance image ID or its original-archive-bound config/manifest equivalent,
remain running, unpaused and not restarting, and preserve
their container/start identities and replica membership across those checks.
Missing services, malformed/duplicate IDs, changed service/project labels and
unavailable Docker inspection refuse acceptance. Only successful image and
web/direct API/Worker/proxied API checks allow the state update. The diagnostic
URLs must still address this same deployment; these reads do not attest routing
or every replica's configuration/readiness. It rechecks original inputs and prior record before
publishing one atomic file containing both `current` and `previous`. The record
contains the complete CI build identity, all three image IDs, input digests and
acceptance time; it contains no credentials, configured URLs or response bodies.
The previous entry preserves the entire immediately prior accepted reference.

A failed image or runtime check leaves both entries unchanged. Reaccepting the
same artifact rechecks images/runtime and preserves record bytes and the prior reference.
A per-state OS lock refuses competing writers and releases on process exit. The
one-byte `.lock` file intentionally persists; leave it in place. Keep this state
on protected storage with supported local file locking and atomic rename, and
retain the corresponding original bundles independently. Copy older records to
the managed release inventory when retention requires more than one prior entry.

This is the final record step in the procedure, not image deployment or CI/
archive attestation. It does not query GitHub, verify image archive bytes, apply
migrations, manage traffic or choose a database rollback. Complete those earlier
checks before treating a recorded reference as known good. After a reviewed
manual rollback and its workflow checks, run this same command against the
selected older bundle; the replaced current release becomes `previous`.

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

### Running image verification — 2026-10-15

The former command can record declared bundle image IDs without proving they
are the images actually running. The repair binds the final acceptance to the
selected Compose project's image IDs as well as the existing runtime metadata.
It lists only that project's three named service families, excludes Compose
one-off containers, and inspects only IDs, image IDs, running/paused/restarting
state, start identity and Compose labels. It never reads environment values,
mounts, application logs or full container configuration. Each Docker read has a
ten-second deadline; failures use the existing private diagnostic and preserve
the known-good reference. Concurrent deployment changes must still be managed
by the operator; the command observes identities before and after runtime checks.

All **31 state/command tests** pass, retaining the original 22 contracts and
adding wrong-image, missing/duplicate identity, state/label, inspection failure,
restart/replacement, repeated-acceptance and initialization refusals. Healthy
replicas are accepted only when every selected replica matches; an incorrect
second replica or changed healthy membership/start identity is refused.
All **six acceptance-driver tests** and **47 release-artifact tests** pass too.
The first local driver run lacks jq in its test PATH; adding the existing
bundled jq resolves that environment failure without a product/test change.

The complete **74.216-second** native run uses fresh PostgreSQL 17.11/all 149
migrations, restricted runtime roles, current compiled `9dd58ac1` API/Worker
and real cached web/API/Worker image IDs. All runtime checks match while the
old CLI accepts each of three incorrect provenance image references. The
repaired CLI rejects each, preserving original record bytes; restored matching
references reaccept without rewriting history. The unchanged complete assembled
acceptance-record phase then passes actual Worker stop/refusal/restart, strict
diagnostic-address rediscovery and matching runtime/image checks. API process
identity and Worker container/image identity remain intact.

All **719 source/test/driver inputs and 71 binary files** match afterward.
Owned containers, volume, network, private scratch files and **40,095 temporary
bytes** are removed, restoring original Docker inventory. No images are built,
pulled or imported, existing local services/database used, external provider
called or large-load test run. The fixture mounts compiled assemblies read-only
and supplies its own metadata/provenance; this proves the operator boundary,
not a complete current immutable-image release or real two-version application
rollback. Exact-commit full CI and the remaining ARCH-12/MVP acceptance apply.

### Atomic acceptance record verification — 2026-10-15

The mandatory [assembled acceptance-record phase](../architecture/release-bundle-startup.md#assembled-acceptance-record-gate--2026-10-15)
now executes the bundled command against running API/Worker services, requires
record preservation through a stopped Worker, and rechecks the same image after
restart. Its current compiled-runtime native rehearsal passes, including actual
ephemeral address rediscovery; current immutable-bundle CI remains required.

All **22 state/command cases** pass, including full prior-reference retention,
failed runtime checks, idempotent acceptance, operator rollback recording,
cross-environment refusal, explicit initialization, malformed/ambiguous/oversized
inputs, provenance mismatch, candidate changes during verification, competing or
noncooperating writers, failed file replacement and private CLI diagnostics.
The immutable bundle cannot contain the mutable state. CI requires these tests
and the bundled command; artifact validation refuses an omitted command even
with regenerated checksums.

The complete real CLI additionally passes **five native Nginx cases**: first
acceptance, wrong-proxy refusal preserving prior bytes, next-release acceptance
preserving the old image IDs, repeated acceptance preserving history, and
recording the prior release after a simulated operator rollback. Actual runtime
checks and file locks/writes run, with no injected verification callback. All
four captured source inputs match; container/image/start identity is unchanged,
private scratch files and 18,987 owned temporary bytes are removed and original
Docker inventory is restored. No application database, existing local service,
new image build/pull/import, external provider or load test is used.

These native identities and health responses are synthetic. They prove the
operator record/runtime boundary, not current application promotion or a real
two-version deployment rollback. Current immutable-image/full CI, managed
deployment, provider configuration and complete ARCH-12 acceptance remain
required. Large load testing stays deferred until after the operational MVP.

## Promote the candidate

1. Require completed successful CI for the exact candidate revision, including required integration/security/release gates. Verify the retained image/security identities and checksums using the release guide. Load those original three images; do not rebuild them on the deployment host.
   Release Compose requires all three image references and never automatically
   pulls application images. An explicitly verified registry digest can also be
   fetched before starting the deployment; omitted or empty references fail
   configuration instead of selecting a development image.
2. Review migrations and application compatibility before applying changes. Forward-only or irreversible changes need a phased procedure and verified recovery plan. Do not assume an older image accepts a newer schema or reverse SQL files automatically.
3. Preserve the same project name and environment configuration. Set the three image references to the verified candidate values. Apply the candidate's migrations and provision restricted roles from its release directory, as documented in the release guide.
4. Start the candidate and run `bash verify-release-runtime.sh build-metadata.json` from its verified bundle. It requires health/readiness for web, API, Worker and the web-to-API proxy, then matches web, direct API, Worker and web-proxied API build identities to that bundle. Configure `STRATAAI_WEB_URL`, `STRATAAI_API_URL` and `STRATAAI_WORKER_URL` for this same deployment when the loopback diagnostic defaults do not apply. The HTTPS-only overlay keeps API/Worker private; use approved private diagnostic addresses rather than opening their ports. Validate sign-in, a persisted Board/List/Card workflow, authorized collaboration and enabled storage/mail providers through the deployment's configured origins.
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
fails before HTTP. Six health/readiness/proxy responses and all four build
responses must be HTTP 200 without redirects. Every served revision/version
must equal the selected bundle. API and Worker must additionally report their
respective `strataai-api` / `strataai-worker` service identities and `production`
mode. Healthy Demo or mixed-mode hosts cannot satisfy this Production check.
The fourth metadata response is `/api/runtime` through the configured web
origin, with the same API service, Production mode and selected revision/version
requirements. Direct API success cannot hide an older or incorrect web upstream.
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
