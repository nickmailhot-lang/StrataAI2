# Actual scanner engine runtime verification

## Durable provider outage recovery — 2026-10-15

Two new bounded contracts compose the actual background processor, restricted
PostgreSQL queue and scan-delivery adapters. One private object read throws;
the other scanner consumes the source and reports unavailable. Each persists
one safe retry code, leaves the file Pending with unchanged digest/size/revision,
and publishes no verdict audit/event or Card revision. The first processor's
connection factory is disposed before recovery.

A fresh processor and connection factory recover the same durable job on attempt
two after only the fixture's retry clock is advanced. The original canonical
metadata, source identity and bytes are preserved. The file becomes Clean with
one revision, the Card advances once, and exactly one audit and ready event are
committed. A further processor poll is empty and performs no provider I/O.
The storage fixture refuses writes/deletes and checks the exact source reference.

The complete bounded attachment persistence invocation passes in **17.954
seconds**, including both new cases and existing upload, lease, preview, cover,
recovery and lifecycle contracts. All **149 migrations** are applied to an owned
temporary PostgreSQL instance, with actual restricted API/Worker logins. The
solution builds with zero warnings/errors. All **819** captured source/binary
inputs match; cleanup removes both owned containers, their internal network and
724,100 temporary bytes, restoring the original Docker inventory. An initial
fixture used an unsupported unencrypted hostname and was correctly refused;
the passing capture uses the supported private `postgres` alias without changing
transport policy.

Only object/scanner responses and actor admission are synthetic. This proves
durable database-backed retry composition, not deployed object storage/IAM,
actual engine outage recovery, HTTP authorization or current immutable images.
Current assemblies run read-only in the retained Linux runtime; no images are
built, downloaded or imported. Large-load tests remain deferred until after the
operational MVP. PRD-14 remains open at **32% estimated work remaining**.

See [the recovery contract](../../tests/StrataAI.Persistence.Contracts/AttachmentProviderRecoveryContract.cs)
and [the existing mandatory CI invocation](../../.github/workflows/ci.yml).

## Attachment readiness outages and recovery — 2026-10-15

The new **six-case** readiness acceptance class passes in **12.809 seconds**
without product changes. Three HTTP cases withdraw readiness for unavailable
database, unsafe bucket privacy and a private provider permission failure, then
recover with the same checker, HTTP host and unchanged runtime identity. Exact
provider-read counts prove database short-circuiting and current privacy checks.
Repeated `/readyz` failures are 503 with exactly the four safe status/code/message/
correlation fields; account/provider details remain absent. `/healthz` stays 200
and makes no provider calls.

A deliberately cancellation-ignoring bucket operation still produces the bounded
503 before the independent four-second watchdog. Its actual dependency token is
cancelled by the existing two-second budget. Releasing the late result cannot
send the next provider request; the same checker later recovers. A separate
caller-cancellation case propagates cancellation rather than inventing an unready
result and also recovers after the held operation is released.

The Worker-style checker uses the actual ClamAV adapter and a small Unix socket
protocol fixture at one owned path. It passes PONG, then refuses the missing
socket, malformed reply and stalled reply, and accepts a replacement PONG server
at the same path. The outer two-second budget dominates the scanner's configured
30 seconds; all five probes retain current bucket checks. This is readiness
protocol evidence, not an actual engine/signature or durable job execution test.

The HTTP host deliberately uses controlled Demo composition with the actual
Production dependency checker injected; database readiness and the SDK S3 client
are controlled. There is no real PostgreSQL/AWS bucket, IAM identity, malware
engine, upload, verdict publication or image rebuild in this capture. Production
registration and role/lifecycle behavior still require their own evidence.

The complete solution builds with zero warnings/errors after correcting the new
fixture's cancellation-token cleanup calls. An initial zero-case run used the
previous assembly after the first build failed; it is discarded. The final
runner explicitly requires six executed cases with zero errors/failures/skips/
unexecuted cases. Captured source/binary hashes match and owned cleanup restores
original Docker inventory; Unix socket files and the test container are removed.
Current compiled assemblies execute with network disabled and read-only source/
test mounts in the retained Linux runtime. This is not current immutable-image
CI or deployed-provider acceptance. Large-load testing remains deferred until
after the operational MVP. PRD-14 stays open at **32% estimated work remaining**.

See [the acceptance tests](../../tests/StrataAI.Api.Tests/AttachmentReadinessRecoveryTests.cs)
and [operator configuration](../release/README.md#optional-private-file-attachments-on-linux).

## Actual engine verification scope

PRD-14's scanning/quarantine boundary needs separate evidence for its protocol,
engine and publication workflow. The existing enabled-provider browser fixture
uses an explicit reply simulator; it does not establish actual engine behavior.

`scripts/ci/test-real-attachment-scanner.sh` starts the official ClamAV base image
pinned to digest `sha256:7769870154c74ce31b0047dd8771e81f7c4269278bc005782e9e419e4922c73d`.
It follows ClamAV's [local socket configuration](https://docs.clamav.net/manual/Usage/Configuration.html)
and [custom signature database](https://docs.clamav.net/manual/Signatures.html)
interfaces. A disposable database contains exactly one hash signature for a fixed
harmless acceptance sample. It downloads no official signature database, exposes
no TCP listener and runs without network access. The named volume contains only
the public fixture configuration/signature and private local socket.

The already-built Worker runs `--verify-attachment-scanner-runtime` before host
startup, configuration, credentials, database connections or job execution.
It requires actual daemon readiness, clean verdict for a 150,003-byte sample
spanning multiple INSTREAM chunks, infected verdict for the harmless test
signature, empty-input refusal, caller cancellation and clean recovery. It also
requires caller-owned streams to remain readable and fully consumed.
Before creating the daemon, the same Worker command runs without a socket mount.
CI requires exit 1 and the fixed failure marker: success, a timeout, a crash or
another refusal code cannot substitute for the required absent-provider outcome.
The updated complete local invocation passed both this missing-daemon negative
control and the real-engine positive checks on the same pinned Worker image;
the process exited 0. This verifies the standalone command's refusal, not durable
attachment recovery during a scanner outage.
Only fixed outcome text is printed. No client filenames, user files, IDs,
provider diagnostics or database state enter this check.

Both containers use read-only roots, dropped capabilities, no-new-privileges,
bounded memory/CPU/process counts and private scratch. The Worker mounts the
socket volume read-only. A strict shell, bounded command deadline, fixed success
marker and owned-resource cleanup govern the invocation. This Linux Docker check
consumes the existing image and never rebuilds source.

CI requires it in `build-images-once` after the Worker build and before image
export. Six workflow regressions protect against omission, unconditional success,
skip, ignored failure, another image or verification after export. Combined
workflow/metadata checks pass 157/157; the warning-as-error Worker build passes.
Actual image execution is recorded separately below.

## Local exact-image engine evidence, 2026-10-09

At `1176d4929ac5c44471f48ec1ac3034edd47f6b87`, the clean checkout built all three
application images once with local version `0.1.0-local.1176d492`, one captured UTC
creation time and source/revision metadata. All three distinct image IDs and all
four OCI provenance fields matched the explicitly local manifest. This is local
evidence, without a fabricated workflow run identity.

The complete shell invocation passed against that Worker and the digest-pinned
real ClamAV engine. Actual readiness, multi-chunk clean input, deterministic
test-signature detection, empty refusal, cancellation, caller stream ownership
and clean recovery all passed. The process exited 0; independent Docker inventory
showed no owned daemon containers or socket volumes remaining afterward.

The complete disposable attachment fixture now passes on those pinned app images
with the actual daemon and deterministic harmless hash/body signatures. Actual
upload progresses through the restricted Worker, native contained decoder and
preview publication. All 21 browser cases pass: identity 1/1 (27.0 seconds),
original phase 2/2 (96.5 seconds), expanded phase 18/18 (701.8 seconds). Every
report has zero unexpected, skipped or flaky cases, top-level errors and retries.
The intact desktop/phone quarantine cases require authoritative rejection and
refused normal delivery; supported-format, original-reply recovery, source
archive/reconnect and invalid/untrusted-content cases remain enabled.

Subsequent HTTP checks pass concurrent original-key selection, PNG sanitization,
source archive/tombstone, independent Board image ownership, Board archive,
public/private withdrawal, logout/readmission and retirement. The full process
exits 0. Independent cleanup confirms zero owned containers, cloned databases,
provider volumes and credential files; the three original services survive.

The same build-once images were exported and reloaded without rebuilding.
All 49 content-addressed archive blobs and descriptor references verify; the
three archives total 504,939,280 compressed bytes. Post-test verification matches
the original three image IDs, all four OCI labels and archive hashes. This is
explicitly local evidence pinned to 1176d492, not execution of the later CI
provenance-record changes. Official definition coverage, deployed bucket
acceptance and current full GitHub CI remain separate evidence requirements.

This establishes only the defined engine transport checks. A custom test
signature does not establish current official signature coverage, malware
detection efficacy, operational definition updates, the full quarantine/publication
role/outage/lifecycle matrix, deployed provider acceptance or full release CI. PRD-14
remains open; estimated work remaining is **32%**.
