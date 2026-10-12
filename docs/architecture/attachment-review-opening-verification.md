# Archive and cover review opening during Board reads

The retained `baad6b8c` full-browser failures occur after review launchers are
activated: Review attachment archive is absent after Manage attachments, and the
cover candidate is absent after Review Card cover. These controls intentionally
disable launchers and reject loaded reviews while parent access is being checked.

The archive scenario now confirms the archive toggle after Manage and the preview
control after archive review using the existing five-second read-only opening
helper. Cover review confirms its candidate before selection and its removal
control before staging removal. Keyboard input is preserved. Preview/download
activations, byte counts, public consent, cover mutation bodies and exact original
retry keys/counts, pending-command disabled controls, focus, accessibility,
withdrawal and unchanged canonical backing data assertions remain. The original
120-second scenario deadlines and release rate pacing are unchanged.

## Reproduction and verification boundaries

The complete unchanged four-case baseline with the correct Worker policy passes
in 179,342.704 ms. This alone does not repair the hosted failures. A declared
diagnostic invokes the existing read-only Board refresh while the Card modal is
open, delays only its first real request by 600 ms, then falls through to the
original handlers. Original simulated metadata, byte and cover-response payloads
are preserved; no server-side command is repeated. The baseline diagnostic
reproduces absent archive toggles and absent cover candidates on both viewports:
0/4 pass in 403,353.645 ms under the original deadlines, without skips/flaky cases.

An initial local baseline also passes 4/4, but used a different Worker verified-
account policy. It is excluded from the matching hosted-profile acceptance claim.
The corrected API profile intentionally permits the scenarios' unverified-email
accounts; the Worker retains verified-account policy. The earlier profile mismatch
is a fixture issue, not a product repair.

The repaired declared delayed diagnostic passes all four cases in 181,627.992 ms,
with zero failures, skips or flaky cases. The complete ordinary repository invocation passes all four cases in
176,843.259 ms, without diagnostic injection, failures, skips or flaky cases.
Both final captures independently have zero mismatches across 2,085 hosted
source/runtime hashes, 1,414 native source hashes and four web assets. Owned
containers, database/roles and temporary credential files are removed;
original stopped PostgreSQL data is preserved. All 36 attachment-management/cover component cases pass,
including existing access/version and pending-intent guards. Browser type-checking,
zero-warning solution build and all 213 immutable CI coverage guards pass.

Fixtures use real restricted PostgreSQL/RLS, the API and separate Worker for the
canonical Board. Attachment publication, options, image bytes and cover receipts
remain the scenarios' explicit simulations. Both scenarios require unchanged
canonical backing attachment/Card state; neither establishes managed storage,
scanner or upload-through-Worker production acceptance. Compiled runtimes and
web assets are mounted in pinned runtime/edge images, not build-once retained
candidate certification. Current full immutable CI and the remaining
[attachment acceptance](attachment-acceptance.md) govern closure. Estimated
PRD-14 work remaining stays **32%**.
