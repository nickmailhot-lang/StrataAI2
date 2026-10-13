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

## Authority delivery before replay snapshots, October 13, 2026

The retained full browser shard 4 from [CI run 38358227296](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38358227296), commit `a860af3db43b5dd853a85f2288b71beda8765c99`, fails all four permission-order roles at the complete private-state comparison. Artifact `11802392133` has SHA-256 `454d69d71bcac75ce7d7080745ab25369ef5faa4a87c33fc6f46364641ffe427`. Its atomic diagnostics identify only the jobs component: `INVITATION_RECIPIENT_AUTHORITY_PAGE` changes `attempt_count`, `state`, `updated_at` and `version`. The other eight components match.

The Board authority producer in migration 107 enqueues these jobs for original membership and visibility changes. The separate discovery Worker processes and acknowledges them asynchronously. The previous baseline could therefore capture unfinished work from the preceding successful command and compare it with a later snapshot after delivery advanced. These hashes alone did not attribute the change to the subsequent denied command or replay.

The scenario now positively requires the original fixture Organization's invitation-authority jobs to exist and all succeed before each of its three full-state baselines. Each delivery observation has a 30-second bound within the unchanged 180-second scenario deadline. Pending or failed delivery cannot pass. All nine original components, complete job records, original comparisons, command ordering, retry keys/bodies, receipt and notification counts, and permission withdrawal/restoration remain intact. The Worker continues with its original handlers and discovery settings; the observation does not change stored state or Worker configuration.

Supporting verification runs all four complete roles against restricted PostgreSQL/RLS, the compiled Production API and separate Worker: **4/4 passed**, zero skipped/flaky/unexpected, 524,578.054 ms. A separate declared one-role negative probe changes an authority job's `last_error_code` after the new delivery checkpoint. The original full-state assertion fails as required in 39,660.500 ms, and its seven-field metadata-only diagnostic identifies exactly that job type/column. The initial negative fixture hit a Windows path-length limit during setup and is excluded from product evidence; the shortened fixture supplies the negative result.

Both terminal captures independently match Primary across 2,109 hosted source/runtime hashes, 1,414 native source hashes and four web assets. Shared read-only runtime mounts avoid repeated runtime copies. Owned containers, database/roles and temporary credentials were removed; the original stopped PostgreSQL data remains preserved. Mounted local verification supports this fixture repair; complete current immutable-image CI and release acceptance are still required. The separately observed hosted tablet capacity drag failure remains unresolved. Estimated work remaining stays **14% for PRD-05** and **15% for PRD-17**; both remain open.

## Native input channels for capacity drag triage

The same retained CI run's tablet capacity case fails the exact destination's `data-card-drop-over` assertion. Capacity artifact `11797028542` has SHA-256 `ba72ff903fe63aaf3ed752774db700a6f338d685139b7cee98991e3f0a2e251d`. Its numerical scroll evidence retains the earlier edge pointer while the protocol trace records later mouse moves. That evidence does not establish whether the later input was absent, intercepted before bubbling, cancelled or otherwise unavailable to the drag handlers.

Capacity diagnostic schema **3** adds six bounded channels: capture and bubbling observations for pointer and mouse movement, plus captured pointer cancellation and release. Each retains only a count and its latest finite timestamp, coordinates and button state. There is no input history or retained event/target object. Original scroll samples and their bubbling-pointer observation remain unchanged, with the existing 2,048-sample bound and original native scroll delegation. All original capacity actions, exact-target drag assertions, write counts, persistence, viewport and keyboard/focus requirements remain intact.

All eight mandatory diagnostic checks pass, including capture-only movement, private-field exclusion, finite values, bounded retention and native scrolling/exception behavior. A separate Chromium probe deliberately stops pointer bubbling: the new channels observe two captured pointer moves, zero bubbled pointer moves and two bubbled mouse moves, while native scrolling remains correct. This verifies diagnostic discrimination, not the cause of the hosted failure.

The complete original rank script and desktop/tablet/phone capacity scenarios pass **3/3**, zero skipped/flaky/unexpected, 192,711.951 ms for the browser phase. They use actual persisted 200-list/5,000-active-card fixtures and preserve all 100,000 complete archived records. Independent verification finds zero mismatches across 2,109 hosted source/runtime hashes, 1,414 native source hashes and four web assets. Retained schema-3 evidence from all three cases has exactly six finite-point input channels; each observes 47 captured and 47 bubbled pointer moves. Browser types, a fresh solution build and 213 immutable CI guards pass. All owned containers, database/roles and temporary credentials were removed; original PostgreSQL data remains preserved.

The hosted tablet failure did not reproduce in this ordinary capture and remains open. Complete current build-once CI and retained-image release acceptance are pending. Estimated work remaining stays **34% for PRD-01**, **15% for PRD-04** and **25% for PRD-06**.
