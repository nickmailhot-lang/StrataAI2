# Permission job columns and URL draft failure evidence

The retained `914d5c4f` fourth browser shard passes 77 cases and fails five:
both URL attachment viewports and MEMBER, ORGANIZATION_READER and PUBLIC_READER
permission-order cases. ADMIN passes. The three permission failures change only
the jobs component; the other eight components match. Both URL cases fail the
first create-button enabled assertion before any attachment write. These
failures remain unresolved; this change improves evidence rather than claiming
a product repair.

## Diagnostic contract

Permission comparison preserves the complete original nine-table snapshot,
row ordering, MD5 and all three full equality checks. The same SQL statement
also groups job columns by job type and hashes ordered column-value arrays
from that snapshot. On failure, the existing attachment includes before/after
column hashes and changed-column names. Job types and column names are visible;
record values, IDs, payloads and credentials are not serialized. No table or
column is excluded from the original comparison. Worker processing, command
ordering, original receipts and scenario deadlines remain.

Only after the original URL-create enabled assertion fails, a separate
attachment records whether the current inputs match the declared fixture
values, whether create is enabled, and whether revision-conflict or access-check
messages are visible. Each input observation is bounded at 500 ms; unavailable
observations are null. It attaches no input text or account/request/response
contents. These are observations after failure, not an atomic snapshot of the
instant that failed. The original error is rethrown; diagnostic attachment
failure does not replace it. Successful cases take their original path.

## Verification, October 12, 2026

The complete unchanged URL file passes 2/2 in 69,513.397 ms. A declared probe
invokes the existing read-only Board refresh after original draft typing and
delays its first real request 600 ms, then falls through to the original
handler. Its initial pending-state check incorrectly expected a disabled
control; the form is hidden during checking. That capture is excluded from
product reproduction evidence. The corrected complete probe passes 2/2 in
66,800.706 ms with original values, commands, retry keys and deadlines intact.
Thus that timing condition does not reproduce the hosted failure.

The ordinary diagnostic change runs both complete original files: all six
desktop/phone URL and four permission-role cases pass in 577,298.437 ms,
zero failures, skips or flaky results. Browser types, a fresh solution build
and 213 immutable CI guards pass. Mechanical comparison verifies that the
original nine-table snapshot expression and all three equality sites remain.

Separate negative controls verify failure evidence, not product acceptance:

- Changing one disposable WORK_EVENT_READY job's last_error_code after the
  committed snapshot makes the original MEMBER assertion fail. Exactly one
  attachment identifies jobs and only WORK_EVENT_READY.last_error_code.
  All nine component hashes and every grouped column value are checked as
  32-character hashes. The expected-failure case takes 39,496.658 ms.
- A diagnostic-only DOM observer keeps the disposable create control disabled
  after original draft typing, including replacement controls. The original
  enabled assertion fails and produces exactly one attachment with the seven
  declared fields: viewport width and fixture-match/status booleans. No raw
  inputs appear. The expected-failure case takes 35,663.474 ms. An earlier
  control affected only a replaced node and the original scenario passed;
  it is excluded from failure-oracle evidence.

The ordinary run and both final negative controls independently have zero
mismatches across 2,102 hosted source/runtime hashes, 1,414 native source hashes
and four web assets. Primary stays frozen during each capture. Shared read-only
compiled builds avoid repeated runtime copies. Owned containers, database/roles
and credentials are removed; the original stopped PostgreSQL is preserved.
The fixtures exercise real restricted PostgreSQL/RLS, API, separate Worker and
browser transport. Optional-verification registration creates verified accounts
without queuing verification email; this is not managed-mail acceptance.
Mounted compiled builds do not certify retained immutable images.

Current full build-once CI, retained release evidence and full
[attachment](attachment-acceptance.md) and
[notification](prd-17-acceptance.md) requirements govern closure.
Estimated PRD-14 work remaining stays **32%**, and PRD-17 stays **15%**;
both issues remain open.
