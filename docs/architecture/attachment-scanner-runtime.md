# Actual scanner engine runtime verification

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

The complete disposable attachment fixture is also running on those pinned app
images with the real daemon. Its initial upload has already progressed through
the restricted Worker, native contained decoder and preview publication. The full
browser and subsequent HTTP phases are still in progress; no completed result is
attributed to them here. The complete identity and original two-case browser
phases have now passed; expanded cases and later HTTP checks remain live.
Official definition coverage, deployed bucket acceptance
and full current GitHub CI remain separate evidence requirements.

This establishes only the defined engine transport checks. A custom test
signature does not establish current official signature coverage, malware
detection efficacy, operational definition updates, full durable quarantine/
publication acceptance, deployed provider acceptance or full release CI. PRD-14
remains open; estimated work remaining is **32%**.
