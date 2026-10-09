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
Actual image execution must be recorded separately once terminal results exist.

This establishes only the defined engine transport checks. A custom test
signature does not establish current official signature coverage, malware
detection efficacy, operational definition updates, full durable quarantine/
publication acceptance, deployed provider acceptance or full release CI. PRD-14
remains open; estimated work remaining is **32%**.
