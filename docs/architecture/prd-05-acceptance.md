# PRD-05 acceptance and closure audit

This audit covers the complete [PRD-05 issue](https://github.com/nickmailhot-lang/StrataAI2/issues/6), including all ten functional requirements, three acceptance criteria, thirteen test scenarios and the definition of done. PRD-05 remains open. Estimated work remaining is **15%**, a planning estimate rather than a measured completion fraction.

## Functional traceability

The [five-case strict-policy native watch matrix](prd-17-acceptance.md#executed-role-and-visibility-reader-watch-matrix)
adds executed consumer proof for PERM-FR-002/003/008/010: actual ADMIN/MEMBER
grants on Private Boards and Organization/Public read-only visibility without
Board grants. Readers can manage their personal watches while real Card edits
return neutral 404 and canonical title/version remain unchanged. Destination
grant removal or transition to Private withdraws desktop/phone inbox content;
later admitted activity preserves full stored recipient history. All five cases
pass together in 5.2 minutes under strict verification, with tagged Axe/overflow.
This uses current compiled Production services and restricted PostgreSQL schema
112; it does not establish current retained-image or complete permission-matrix
acceptance. PRD-05 remains open at **15% estimated work remaining**.

The [extended native re-admission run](prd-17-acceptance.md#executed-native-watch-re-admission-and-retained-history)
also passes all five cases in 6.1 minutes. The four non-Owner cases regain their
actual Board role or original visibility, recover retained subscription clocks
and original notification history in both already-open inboxes, then receive
exactly one new private notification from a later eligible edit. This strengthens
withdrawal/re-admission consumer evidence while retaining the same current-image,
complete permission inventory and concurrency limits. PRD-05 stays open at
**15% estimated work remaining**.

| Requirement | Current implementation or source evidence | Evidence or work still required |
| --- | --- | --- |
| PERM-FR-001 three visibility values | WorkManagementService validates canonical visibility; BoardVisibilityPage reviews the current Board revision and explains public exposure. | Complete current-image native consent/conflict/recovery and rejected-write persistence assertions. |
| PERM-FR-002 visibility does not grant edits | Server access separates visibility from administration/edit/move capabilities; anonymous native assertions inspect the actual access object and reject mutation. An executed 18-case HTTP matrix covers three visibilities, six access states, content/comments/checklists/URL attachment/List commands, binary upload admission/replay with a synthetic private provider, private member directory and destination edit admission. | Extend the remaining endpoint inventory, real-provider/current-image binary persistence and consumers, and lifecycle/concurrent admission. |
| PERM-FR-003 ADMIN/MEMBER roles | BoardMemberRecord stores role, active state, timestamps and independent version; member commands enforce eligible active Organization membership and reviewed If-Match. | Full invalid/unauthorized/stale/replay matrix with unchanged protected state, restricted persistence and current release images. |
| PERM-FR-004 eligible invitations | BoardInvitationService checks current Board administrator and eligible recipient policy; separate invitation acceptance preserves Organization roles and grants Board membership. | Current retained-image issuance/acceptance, token expiry/revocation, account/issuer/recipient authority changes, replay, registration and private mail/link delivery acceptance. |
| PERM-FR-005 removal safeguards | WorkManagementService enforces last-Board-admin continuity with an explicit Organization-admin override and atomically removes Card assignments. BoardAdminContinuityTests and the reviewed member UI cover these contracts. | Full concurrent/rollback/current-admission checks, native permission withdrawal and current-image acceptance. |
| PERM-FR-006 promotion/demotion | Canonical eligibility, independent member version, continuity safeguards, MUI person-bound consent and read-only uncertain-command recovery. Direct commands emit BOARD_MEMBER_ADDED for new/reinstated grants and BOARD_MEMBER_ROLE_CHANGED for active role changes; executed API and restricted PostgreSQL cases cover audit/journal/outbox identity, receipt replay and late rollback. | Complete mandatory CI and current-image consumer proof, including the complete concurrent role/continuity matrix. |
| PERM-FR-007 anonymous public read-only | Public Board snapshots expose read-only access; controls omit editing and administration. Native visibility and persisted-workflow cases use a separate anonymous browser. | Current retained-image cross-surface read-only acceptance, anonymous cover/background admission and current parent/account/visibility withdrawal. |
| PERM-FR-008 private admission | Current Organization/account admission and explicit private grant checks; inaccessible Boards return neutral non-disclosure. | Complete role/lifecycle/reconnect matrix, including authority loss during reads and commands. |
| PERM-FR-009 Organization discovery | Active directory uses current reader admission, bounded filtering and protected Organization Board replay; ordinary directory native cases were executed locally. | Current-image role/visibility transition, paging, reconnect and large-data acceptance. |
| PERM-FR-010 server authorization | Protected commands use Application authorization inside their owning transaction; PostgreSQL tenant RLS and restricted roles retain database boundaries. | Complete endpoint/operation inventory, refusal-before-disclosure, late rollback and exact-image concurrency proof. |

Paths above are under `src/StrataAI.Application/WorkManagement`, `src/StrataAI.Application/Onboarding`, `apps/web/src/features/kanban` and `tests/StrataAI.Api.Tests`. Relevant behavior guides are [visibility](../board-visibility-ui.md), [member administration](../board-members-ui.md), [public sharing](../public-board-sharing.md), [member directory](../board-member-directory.md) and [live directories](../organization-board-realtime.md).

## Acceptance criteria and scenarios

- **AC-PERM-05-01:** prove authenticated authorized visibility selection, reviewed version and consent, persisted canonical state, and client reflection. An HTTP response or component assertion alone is insufficient.
- **AC-PERM-05-02:** prove role validation and stable unauthorized/invalid/stale refusals without private disclosure or changes to membership, Board state, assignments, audit or receipts.
- **AC-PERM-05-03:** prove genuine separate Worker delivery to another authorized client, missed-event reconnect recovery, canonical source identity and immediate withdrawal after current authority loss, without a manual document reload.

| Test scenarios | Required acceptance scope |
| --- | --- |
| TC-01 happy path; TC-02 empty state; TC-03 invalid input; TC-04 unauthorized user | Visibility, membership and invitations through the actual API, restricted persistence and browser. |
| TC-05 mid-session permission loss; TC-10 archived/deleted parents | Withhold private Board/person data and retire old consent; late responses cannot restore withdrawn admission. |
| TC-06 timeout/retry; TC-07 duplicate request | Original receipts/intent or explicitly read-only uncertain-write recovery; no automatic repeated destructive effects. |
| TC-08 concurrent update; TC-09 disconnect/reconnect | Actual competing revisions and original Worker events; source/event identity must survive reconnect without disclosing ineligible Boards. |
| TC-11 keyboard; TC-12 mobile | Desktop/phone controls, cancellation and submission focus, named person-bound reviews, announcements, overflow and WCAG 2.2 AA checks. |
| TC-13 large data | Documented fixture and actual measured operation/viewport budgets; test duration including rate pacing is not a benchmark. |

## Events, data and cross-cutting completion

The required public event names are BOARD_MEMBER_INVITED, BOARD_MEMBER_ADDED, BOARD_MEMBER_ROLE_CHANGED, BOARD_MEMBER_REMOVED and BOARD_VISIBILITY_CHANGED. Invitation acceptance and direct member administration distinguish new/reinstated membership from active role changes. Same-role direct commands retain BOARD_MEMBER_UPDATED and their existing membership revision behavior. The separate private Demo upsert proof retains BOARD_MEMBER_UPDATED: the consumer resolves both new public names to that owning-command proof without renaming its capture or deduplication identity. Self-demotion uses the proven previous administrator role. Invitation acceptance remains in its own Organization transaction and recipient-source path, rather than becoming an administrative authority command. Restricted PostgreSQL audit/journal/outbox and receipt proof is executed locally below and mandatory in CI. Retained-image consumers still require executed transactional/replay proof.

Canonical memberships retain created/updated clocks and versions; the API directory pages at 50 qualifying rows, filters current profiles and withholds former-member private profiles. Review join-clock semantics, tenant-safe foreign keys/indexes, migration upgrade/rollback and restricted-role execution as part of the data requirements. Review every specified member/invitation/visibility endpoint and its schema, Problem envelope, pagination, idempotency and authoritative revision.

Completion also requires documented telemetry for feature open/use, success/failure latency, permission codes, reconnect/conflict, exceptions and explicit retry, without sensitive content or misuse as business audit. See [sharing telemetry](../board-sharing-telemetry.md).

The four latency targets remain: initial usable Board below 1.5 seconds, movement feedback below 100 milliseconds, server acknowledgment p95 below 500 milliseconds, and cached Card detail below 200 milliseconds. Required supported capacity remains 200 Lists/Board, 5,000 Cards/Board and 100,000 archived Cards/Organization. General readiness, a small native fixture or a different consumer benchmark cannot establish these budgets.

All functional requirements, current server authorization tests, schema/migration review, emitted/consumed domain events, unit/integration/end-to-end execution, accessibility/keyboard, error/loading/empty behavior, telemetry, no known P0/P1 defects and complete product documentation must pass before closure. A whole-ticket green-release and requirement-by-requirement audit remain unproven. Dependencies remain PRD-02, PRD-03, PRD-04 and PRD-24; MUI, the modular monolith, separate Worker, PostgreSQL/RLS and build-once release architecture remain unchanged.

## Current local execution

The initial six-case invocation against the Production API, restricted schema-110 PostgreSQL, fresh production web bundle behind current Nginx/CSP and scoped separate Workers passed both member cases, desktop visibility and both persisted Board workflows. Phone visibility reopened consent after an HTTP refresh but before the competing original Worker event reached the browser; the later invalidation correctly retired that consent. The corrected fixture observes the genuine upstream event ID, matches the canonical visibility event/revision, and waits for its subsequent Board read before opening a new review. It does not insert events, change product consent rules or relax any assertion, deadline, retry or production limiter. The corrected full invocation exited 0 with all six cases passing in 3.5 minutes, including intentional pacing; this duration is not a performance measurement.

Both member cases verify keyboard consent/conflict and lost demotion/removal read-only recovery with unchanged Organization membership and immediate denied recipient access. Both visibility cases verify cancellation/focus, stale-version refusal, renewed public consent, lost-response canonical recovery and anonymous read-only admission. Both persisted-workflow cases use the UI to create an Organization, Board, List and Card, recover a lost Card creation exactly once, preserve a conflicting Card draft, retain saved state after reload, refuse wrong Organization scope and show the public Card read-only to a separate anonymous browser. See the [member evidence](../board-members-ui.md#executed-desktop-and-phone-member-recovery) and [visibility evidence](../board-visibility-ui.md#executed-desktop-and-phone-visibility-recovery). All six scoped Workers were removed after execution; the reusable API/web/database runtime and data volumes remain available.

## Executed direct member event regression

Nine new API cases cover first grants to both roles, promotion, demotion, reinstatement, same-role updates, byte-identical HTTP receipt replay, stale/unauthorized unchanged-state refusals and a late actor-admission failure after mutation/publication followed by original-key recovery. Before the Application correction, seven failed on the old public event name and two passed. After the final correction, all nine pass. Eleven Demo authority cases also pass, including private source compatibility, recipient invalidation on re-grant and self-demotion, rollback and unproven-source refusal. Two invitation acceptance cases and three existing-role preservation cases pass after retaining the separate invitation source boundary and selecting the recipient's grant event independently of earlier fixture grants. The final isolated Release build has zero warnings/errors; these 25 tests have zero failures or skips.

This local API execution uses the Demo in-memory journal. Its audit append is a no-op, so these tests do not prove PostgreSQL audit persistence, restricted-role execution, retained-image delivery or whole-ticket acceptance.

## Executed restricted PostgreSQL member events

`BoardMemberEventContract` executes the registered Production transactional Work service under the restricted API login against the real schema-110 database. Initial account/Organization rows and session admission are declared fixtures. First MEMBER/ADMIN grants, promotion, demotion, same-role update, removal/re-grant and original retry keys prove canonical member revisions, matching audit/journal actor/scope/entity/version/correlation, and exactly one delivery job tied to the actual event ID and Board ID. Complete fixture membership, audit, event, stream, job and receipt snapshots remain unchanged after each replay and after stale/unauthorized refusal.

A fixture-scoped PostgreSQL trigger rejects journal insertion after the member mutation and audit append. The command reports storage unavailability and the complete protected snapshot matches its pre-command state, including stream allocation and retry receipt. After removing the trigger, the original key commits once and its replay changes nothing. The final locked Release build has zero warnings/errors and the expanded Linux/PostgreSQL invocation exits 0. Its disposable container is removed automatically; the active web/API/database and stored volumes remain preserved.

CI now requires this contract in the PostgreSQL source gate through `--board-member-events-only`. These are compiled source contracts in a cached Linux runtime, not retained-current-release API/Worker image or native browser delivery evidence. The whole ticket remains open.

## Executed HTTP and Worker member recovery

`scripts/ci/test-board-member-events.sh` passes locally against the compiled Production API and newly compiled separate Worker in cached Linux runtimes, with real restricted PostgreSQL. It registers and signs in normal owner/member accounts, creates a private Board, and checks first grant, promotion, demotion and same-role event names against the matching audit/source identities. Each identical retry returns the same receipt bytes and preserves membership revision, journal/stream, audit, job and receipt counts. Stale-version and unauthorized self-promotion refusals retain the protected snapshot.

The scoped Worker marks all five canonical Board events ready. A separate ordinary member recovers those original event IDs through `/sync`, receives their ordered public event types with actor IDs redacted, and then gets an empty page at the recovered cursor. An administrator recovers the same event IDs/types/cursor with the admitted actor IDs. The initial fixture incorrectly expected actor IDs in the ordinary member response; all five events had already been delivered. The corrected fixture preserves the product redaction boundary. After actual HTTP removal, that member's recovery is refused without an event envelope while their Organization membership remains unchanged. The final unchanged script exits 0; both disposable API/Worker containers are removed, leaving the original three active services and all volumes preserved.

The script is now mandatory in container integration against the immutable API/Worker images built by CI. Local compiled-runtime execution proves the HTTP/Worker/read path, not retained-current-image or native browser acceptance; the exact-commit CI result remains required.

## Executed HTTP permission matrix

The [HTTP permission matrix](../../tests/StrataAI.Api.Tests/BoardPermissionMatrixTests.cs) executes 18 cases: PRIVATE, ORGANIZATION and PUBLIC crossed with Organization administrator, Board administrator, explicit Board member, Organization member without a Board grant, former Organization member with a still-active persisted Board grant, and anonymous visitor. Accounts register and sign in through the actual host; Organization role/removal setup is declared fixture state. Actual reads validate server access flags and restrict the administrative member directory. Card edits, comments, checklists, URL attachments and List creation verify persisted success or neutral unchanged-state refusal. Refusal snapshots include the owner Board view, journal, comment list, checklist list and attachment list. Moving to a PUBLIC destination without an explicit edit grant is refused for Board administrators/members; the Organization administrator override can edit both contexts. Visibility administration is also exercised for permitted and refused roles.

An Organization administrator without an explicit Board grant can edit content but cannot post comments: COMMENT requires current explicit Board participation. The initial matrix incorrectly conflated those permissions; 15 cases passed and three Organization-administrator comment assertions failed. The corrected expectation preserves the documented product policy and production implementation. For each Organization-member case, an originally refused Card retry key succeeds exactly once after an actual Board grant and its identical replay retains the committed Card revision. The final matrix passes all 18 cases with zero failures/skips; isolated Release compilation has zero warnings/errors.

The same 18 cases now also execute raw binary upload with the existing explicitly enabled synthetic private object provider. Authorized Organization administrators, Board administrators and Board members persist a Pending PDF, advance the Card revision once and receive a receipt without a storage key. Replaying the original key, bytes and expected revision returns byte-identical receipt content, preserves the complete protected snapshot and performs no second provider write. Other roles receive their normal 401/404 refusal with unchanged protected state and zero provider writes or reads. An invalid SHA header is also refused with the same authority status and zero provider calls. This proves admission before claimed-header validation and provider I/O; it does not measure request-body reads. The expanded matrix passes all 18 cases with zero failures/skips in 49.042 seconds; isolated locked Release compilation has zero warnings/errors.

These are required API-host tests with Demo in-memory metadata adapters and an explicitly injected synthetic binary provider; ordinary Demo binary uploads remain disabled. They are not restricted PostgreSQL, deployed S3/scanner or retained-image/native proof for the entire role/operation matrix. Real-provider/current-image binary workflows, remaining endpoint operations, account and parent lifecycle races, concurrent admission, accessibility and capacity/latency remain part of the open acceptance scope.

## Executed controlled download permission matrix

The [controlled download matrix](../../tests/StrataAI.Api.Tests/BoardPermissionDownloadMatrixTests.cs) crosses the same three visibilities and six access states in the actual API host on Linux. Pending files refuse options and delivery without a provider read. With explicitly synthetic Clean metadata, current Organization/Board administrators and Board members can obtain options and SHA-verified original PDF bytes. An ordinary Organization member without a Board grant can read the file only for ORGANIZATION/PUBLIC visibility. A former Organization member with a persisted active Board grant is refused for every visibility, including PUBLIC; an anonymous public Board viewer receives 401 on internal file routes. Refused requests make zero provider reads and disclose neither the filename nor file bytes.

Successful responses retain forced-download, octet-stream and private/no-store headers. Each admitted role then loses Organization membership through a fixture callback after the actual provider stream closes but before delivery. Final admission refuses the response without a download header or PDF bytes; its retry makes no additional provider read. Storage and Clean scanner state are declared synthetic fixtures, while authentication, Linux private staging, integrity verification and HTTP delivery are actual implementations. This does not prove deployed S3/Worker publication or retained-image/browser acceptance. Non-Linux runs explicitly skip this matrix rather than reporting unexecuted assertions as passes. Local Linux execution passes all 18 cases with zero failures/skips.

## Executed restricted PostgreSQL HTTP permission matrix

The mandatory [release permission script](../../scripts/ci/test-board-permission-matrix.sh) passes locally through a compiled Production API using the restricted PostgreSQL runtime login, which has neither superuser nor RLS-bypass privileges. Six accounts register/sign in normally; Organization role/active membership fixtures are inserted explicitly. Three Boards are created through the actual API, with Board administrator/member grants issued through HTTP. The former Organization member keeps an active persisted Board grant after its Organization fixture membership is removed.

All 18 visibility/access cases verify actual Board access flags, the administrative member directory and Card/comment/checklist/URL attachment/List mutations. Every permitted command returns its original receipt bytes and status on replay. Persisted Board, List, Card, checklist, attachment, comment and stream snapshots plus audit/event/job/receipt counts remain unchanged after replay and refused writes. Successful commands change the protected snapshot. Organization administrators without Board participation remain refused for comments while retaining other edit rights; PUBLIC/ORGANIZATION visibility does not confer edits. The final unchanged script exits 0. The disposable API is removed after execution, retaining the original three active services and all volumes.

CI executes this same script against its immutable release API and restricted PostgreSQL, after member event delivery checks. Local execution uses readonly compiled assemblies in a cached Linux runtime, not the current retained release image. Binary storage/scanner, the remaining endpoint inventory, concurrent/lifecycle admission, native delivery/accessibility and capacity/latency remain open. The planning estimate stays 15% until the wider acceptance and exact-commit release checks provide stronger evidence.

## Executed restricted permission receipt recovery

The [permission recovery script](../../scripts/ci/test-board-permission-recovery.sh) passes locally through the compiled Production API and real restricted PostgreSQL for PRIVATE, ORGANIZATION and PUBLIC Boards. An ordinary active Organization member without a Board grant receives `card_not_found` twice with the same original key; Card/member/stream snapshots and audit/event/job/receipt counts remain unchanged. After an actual HTTP Board grant, that original key commits once at Card version 2 and identical retries return its original receipt without further effects.

The owner then commits a different title at version 3. Recovery returns the original version-2 acknowledgment while preserving the newer title/revision and complete protected snapshot. Actual HTTP Board membership removal immediately refuses that saved receipt with neutral `card_not_found`, without returning its title or changing protected state. Reinstating the Board grant permits recovery of the same acknowledgment, preserving version 3 and all post-grant state rather than applying the original command again. All three visibility workflows pass; the unchanged final script exits 0 and shell/diff checks pass.

CI now requires the same transitions against its immutable API and restricted database. Local execution uses the frozen compiled API in a cached Linux runtime, not retained-current-image, separate Worker or native browser evidence. No production authorization policy changes are needed. The disposable API is removed and existing services/volumes are preserved. Estimated remaining work stays 15%; full operation/lifecycle/concurrency, release/native, accessibility and capacity/latency acceptance still govern closure.

## Executed restricted receipt lifecycle extension

The permission recovery script now passes its complete PRIVATE/ORGANIZATION/PUBLIC
invocation against the frozen compiled Production API and real restricted PostgreSQL.
Protected snapshots include the Board and List records as well as the Card,
membership, event streams and audit/event/job/receipt counts. Archiving a List or
Card freezes new Card edits. An admitted original receipt can still be recovered
byte-for-byte without changing archived state or reapplying its earlier title/version.
List restoration is denied to the contributor; Card restoration on an active
List/Board is permitted to that contributor, while permanent Card deletion remains
elevated. An initial fixture incorrectly expected contributor Card restoration to
be denied; it was corrected to the adopted policy, without changing product behavior.

Board archival withdraws both fresh edit and original receipt admission with neutral
`card_not_found`; contributor Board restoration is denied. Elevated Board restoration
lets the original Card receipt be recovered without changing the later authoritative
title or Card version 5. Elevated permanent Card deletion then withholds the original
edit receipt and changes nothing on refusal. Real PostgreSQL records show three final
Card tombstones at version 7. All three visibility cases complete with exit 0; shell
syntax and diff checks pass. The first launch probe preceded API readiness and is
not product evidence. The disposable API is removed; the original three services
and all volumes are preserved.

The API database login is neither superuser nor an RLS-bypass role. Organization
membership is an explicit database fixture; Board grants and lifecycle commands use
actual authenticated HTTP. The existing mandatory build-once CI step now names
lifecycle freezes and requires this expanded script. Local compiled/cached-runtime
execution does not prove current retained images, separate Worker/browser delivery
or all lifecycle endpoints/concurrency. Estimated work remaining stays 15%.

## Deleted List child-receipt admission correction

All three new API regressions initially return an original Card edit acknowledgment
after its parent List has been deleted, despite ordinary child access being refused.
The expanded restricted PostgreSQL fixture independently fails at the same receipt
admission. List tombstones intentionally retain child records, so current Board
authority alone cannot establish a surviving parent for their acknowledgments.

The transactional Card command guard now verifies a surviving non-deleted List for
every Card command acknowledgment, rather than only permanent Card deletion. This
check remains inside the owning transaction/admission boundary. Archived Lists retain
their existing read-only original-key recovery; fresh edits still require active
parents. The correction does not change deletion retention or restore archived/deleted
records on retry.

The final locked Release build has zero warnings/errors. All three visibility API
cases now pass, proving original edit/restore receipts and fresh commands are refused
without content disclosure or changes to retained List/Card state. The existing
archived-parent lifecycle and concurrent retry/revocation cases also pass: five
selected cases in total, zero failures/skips. The complete expanded real PostgreSQL
script passes all three visibilities against the corrected frozen Production API.
It verifies a deleted List at version 3 with its retained active Card at version 4,
withholds both child receipt types, refuses fresh edit/restore, and preserves complete
target Board/List/Card/member/stream snapshots and audit/event/job/receipt counts.
Earlier archive/grant/revocation/recovery cases continue to pass in that invocation.

The existing mandatory build-once CI step executes the expanded script. The disposable
API is removed and the original three services/volumes remain preserved. This local
compiled/cached-runtime execution does not prove current retained images, separate
Worker/browser delivery or full endpoint/concurrent parent-withdrawal acceptance.
Estimated remaining work stays 15%; performance and accessibility remain required.

## Deleted-parent sibling receipt coverage

The complete restricted PostgreSQL permission-recovery script now also passes
comment, checklist and URL-attachment creation/recovery for PRIVATE, ORGANIZATION
and PUBLIC Boards. Each child command commits under current member admission and
its exact original acknowledgment replays byte-for-byte without further effects.
After permanent List deletion, both the original intent/key and a fresh intent
using the current Card version return HTTP 404 with the endpoint's stable neutral
code. Responses exclude the private child content. Exact Board/List/Card/member,
child rows and stream snapshots, plus audit/event/job/receipt counts, remain
unchanged after every refusal. The retained Card reaches version 7 with one child
of each tested type; this extends the earlier version-4 edit/restore fixture.

The first fixture execution incorrectly expected HTTP 201 for the child endpoints;
their established contract is HTTP 200. After correcting that assertion, the full
three-visibility invocation passes with exit code 0. No production policy or API
code changed. This proves local compiled Production API/restricted PostgreSQL
behavior, not current retained-image, separate Worker/browser or concurrent
withdrawal acceptance. The mandatory build-once CI script includes these checks.
PRD-05 remains open at **15% estimated work remaining**, a planning estimate.

## Current local shared performance gaps

The [2026-10-07 desktop/phone diagnostic](../kanban-performance.md#local-desktop-and-phone-diagnostic-2026-10-07)
executes the existing three-List/fifty-Card benchmark through the compiled
Production API, real restricted PostgreSQL and Nginx. Deferring automatic drop
review controls preserves first-save status and original uncertain-response
recovery; 23 selected move/drag tests, TypeScript, lint and web build checks pass.
The latest [registration/observation follow-up](../kanban-performance.md#drag-registration-and-readiness-observation-follow-up-2026-10-07)
keeps drag/drop nodes registered across renders and observes the qualified second
Board response without polling delay. All 43 selected component tests and four
read-tracker tests pass, with unchanged admission checks and timing budgets.
The subsequent [canvas column reuse follow-up](../kanban-performance.md#canvas-column-reuse-follow-up-2026-10-07)
avoids recreating columns on dialog/status changes while retaining immediate
authoritative refresh and move-preview updates. All 73 selected screen, filter,
move-preview and virtual-window tests pass. The final native invocation remains
failed: desktop feedback is 120.4 ms against 100 ms; desktop/phone cached detail is
275.3/336.6 ms against 200 ms. Phone feedback is 92.7 ms, readiness is 1125.2/962.0 ms
and twenty-sample mutation p95 is 210.0/175.4 ms. Desktop assertions stop at feedback;
the report also retains its failing detail measurement. This is local cached-runtime
evidence, not current retained-image release acceptance.
The reporter explicitly distinguishes unverified runtime provenance. The planning
estimate stays 15%; full performance acceptance remains outstanding.

## Remaining implementation order

Current-commit [CI 37691987814](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37691987814)
passed 1,901 component tests but failed the 404 comment-review refusal's immediate
subscription cleanup assertion. Refusal content can commit before passive-effect
cleanup finishes. That fixture now awaits the actual single cleanup call, retaining
all three 401/403/404 cases, disabled/private-content checks, unchanged subsequent
background read count and zero writes. All 30 comment-control cases pass locally,
as does targeted lint; a new complete CI run must verify the repair. Production
cleanup/admission policy and test deadlines are unchanged. The separate cached
editor observation diagnostic above does not establish timing acceptance. Estimated
work remaining stays 15%.

The [cached-detail CPU diagnostic](../kanban-performance.md#cached-detail-cpu-profile-and-modal-lifecycle-guard-2026-10-07)
identifies transition layout work but does not establish timing acceptance.
A proposed appearance bypass fails actual modal retirement and canvas focus
restoration, so it is reverted. The retained screen regression requires an enabled
cached editor followed by UI close, restored canvas accessibility and original-link
focus. Performance changes must preserve this lifecycle contract. Production
behavior and the standard timing benchmark remain unchanged; remaining work stays 15%.

1. Verify mandatory restricted member-event CI and execute current release consumer delivery/reconnect; local API and restricted PostgreSQL audit/journal/outbox, replay, no-op, rollback and private authority compatibility checks are complete.
2. Complete current-image invitation/member/visibility administration, actor and parent authority withdrawal, continuity and reconnect acceptance, including the full operation matrix.
3. Finish WCAG 2.2 AA, documented large-data and latency execution, then audit every definition-of-done requirement against the retained build-once release before closing the ticket.

Local Workers have only their newly created fixture Organization scopes, with all four global discovery loops and identity mail disabled. Frozen API/Worker assemblies mounted read-only in cached runtime images and the ordinary unverified-account browser policy do not establish retained-current-image release proof or strict verified-account acceptance.


## Validated pending head and native invitation recovery

The history bootstrap now accepts the first validated Board stream head, including
pending/reset, before its independently protected account/administration/history/
account read. A bare recovering status still admits no data. This removes a
loading deadlock when event delivery is pending; it changes no server authority,
receipt or command semantics. The payload-free stream callback fires once and
excludes malformed/foreign pages. Creation's native keyboard fixture waits for
the actual initial permission refresh before activating its single command.

All 121 stream/history/creation component cases pass, plus web/browser TypeScript,
targeted lint and production build. Both desktop/phone issuance/lost-revocation
cases pass in one 1.2-minute invocation; both Board account-uncertainty cases pass
in a separate 1.0-minute invocation against the same frozen Production web/API,
restricted PostgreSQL and Nginx fixture. Scoped event delivery is intentionally
absent in this pending-head probe. Account uncertainty before a command sends no
revocation; uncertainty after commitment retains explicit recovery. See
[validated pending head](../invitation-history.md#validated-pending-board-head)
for details, corrected local fixture assumptions and evidence scope.

PRD-05 remains open. Its estimated remaining work is **14%**, a planning estimate.
The historical exact-image browser run 37683742977 has 43 failed scenarios; current
source attribution/repair, automatic event delivery and complete current release
and PRD-wide acceptance are still required. This local slice does not establish
a green immutable release or satisfaction of all acceptance criteria.


## History account/deadline bootstrap verification

All eight Organization/Board desktop/phone account-uncertainty and aggregate
revocation-deadline cases pass in one 4.1-minute native invocation after requiring
the actual stream head and subsequent protected history read before starting the
fault scenario. Baseline: seven pass, one Organization account case fails after
initial live bootstrap restores freshly authorized history during uncertainty.
The passive fixture correction retains every private-withdrawal, no-command,
one-revocation, canonical-history, no-reload and accessibility assertion. Eleven
observer tests and browser TypeScript pass. See
[executed history admission](invitation-administration-ui.md#native-history-account-and-deadline-admission)
for runtime scope and limitations.

Estimated work remaining stays **14%** (planning estimate). The issue stays
open: current immutable-image CI and full PRD-wide acceptance remain required.


## Production history expiry and Organization administration execution

The complete native eight-case invocation passes in four minutes: six corrected
Internal/Portal/Board desktop/phone expiry cases and two unchanged Organization
administration cases. Expiry consent waits for the actual scoped live head and
subsequent protected history read; the baseline completed five passes/one phone
Portal failure before dialog review. Original expiry, withdrawal, zero-write,
canonical-history, no-reload and accessibility assertions remain. Administration
proves actual lost-response commitment, identical-key/body retry across reload,
one invitation, preference recovery/focus, actual member-added source/refresh and
Portal grant separation. Browser TypeScript and documentation checks pass.
See [expiry evidence](../invitation-history.md#production-expiry-consent-admission)
and [administration evidence](invitation-administration-ui.md#production-organization-administration-verification)
for full runtime scope. Provider sending and retained-current-image verification
remain separate; this does not prove every acceptance criterion.

Estimated remaining work stays **14%** (planning estimate). The issue stays
open pending full current immutable CI and outstanding PRD-wide requirements.


## Observed notification permission withdrawal ordering

The [executed permission-order matrix](prd-17-acceptance.md#executed-permission-withdrawal-and-activity-ordering)
adds eight actual races for private Board Member/Admin removal and Organization/
Public reader visibility becoming private. Actual queued database blockers prove
which HTTP command commits first. Withdrawal first excludes future notification
creation; activity first retains one deduplicated notification while current
permission checks withhold inbox/sync/watch reads and individual/bulk read actions.
Real re-admission recovers the exact retained watch and private history. Denied
read actions and original Card-command retries leave the complete protected graph
unchanged. Readers have view permission without edit permission; the Board Admin
fixture has administration permission. This strengthens PERM-FR-002/005/008/010,
NOTIFY-FR-008/012 and PRD-05/17-TC-04/05/07/08.

These are strict verified-account HTTP/PostgreSQL originating-transaction checks,
not additional native transport, keyboard/mobile or capacity acceptance. The
required immutable-image CI phase includes them; local evidence does not establish
a green retained release. Full current CI and remaining PRD-wide acceptance remain
required. PRD-05 stays open at **15% estimated work remaining** (planning estimate).


## Actual read commands across permission withdrawal

The [sixteen actual read-order races](prd-17-acceptance.md#executed-read-and-permission-withdrawal-ordering)
cover Member/Admin grant removal and Organization/Public visibility withdrawal,
single and two-source bulk selection, and both command orders. Withdrawal first
rejects the entire selection without read/journal/receipt changes. Read first
retains its first-read clocks while current authorization refuses the original
cached receipt after withdrawal. Actual re-admission recovers exact history and
allows only the currently authorized read. Full protected graph and stored source
attribution checks pass. The expanded five-case strict local invocation passes in
eight minutes. This strengthens PERM-FR-005/008/010 and TC-04/05/07/08; native
transport/interaction/capacity and full current retained-image CI remain separate.
PRD-05 remains open at **15% estimated work remaining** (planning estimate).


## Board invitation creation and source-frame history admission

The [Board invitation admission correction](../board-invitation-release-evidence.md#board-creation-and-history-admission-after-real-source-frames)
replaces a weak two-read setup assumption with the actual scoped creation head and
protected Board read after it. It also observes actual issuance frames and
subsequent protected history reads before consent. Original request recovery,
exact actor/body/key, cancellation/return focus, canonical rows, current access,
live preference/acceptance and reconnect assertions remain. Four new passive
observer regressions pass with the existing seven. No product guard is weakened;
strict/provider and full current retained-image acceptance remain required.
PRD-05 remains open at **15% estimated work remaining** (planning estimate).

## Invitation withdrawal after current Board role events

The current membership producer's `BOARD_MEMBER_ADDED` and
`BOARD_MEMBER_ROLE_CHANGED` families now reach the PostgreSQL recipient authority
pipeline, preserving the original source IDs and bounded leased delivery.
Both unchanged native Board invitation cases pass issuer downgrade and independent
protected acceptance denial. The routing fixture reproduces the missing family
before repair and verifies deduplicated restricted delivery after migration 113.
See [native recipient evidence](browser-recovery-ci.md#current-board-membership-authority-and-actual-recipient-interruption).
Estimated PRD-05 work remaining stays **15%** (planning estimate); full current
immutable CI and remaining acceptance still govern closure.

The [schema-114 ordering invocation](notification-audit-clocks.md#watch-clocks-and-observed-permission-ordering)
also passes all thirty observed request pairs in five cases, with strict verified
sessions and no skips/retries. The notification oracle now checks persisted update
clocks after grant/visibility re-admission and reads. Denial and original retries
retain complete protected state and first-read history. Separate stored watch
clock/identity/version checks pass for Card, List and Board watches. This is
current compiled Production HTTP/restricted PostgreSQL evidence; browser input,
private transport, provider, capacity and current immutable/full CI remain
separate. Estimated PRD-05 work remaining stays **15%**; the issue stays open.

## Strict native visibility and member administration

The unchanged desktop visibility scenario fails immediate login under real
verified-email enforcement (expected 200 / actual 403). Both visibility cases,
both member-consent cases and the two-administrator live case now use the
existing shared strict account fixture, proving pending-account refusal,
verifying only a freshly registered disposable account and logging in normally.
Ordinary unfiltered full-suite behavior is preserved without the strict flag.
Production policy is unchanged; provider delivery remains separately tested.

The mandatory Board phase now selects twelve complete files / 28 cases,
retaining the previous 23 and adding all three permission files. Existing
public read-only denial, reviewed consent/conflict, same-key recovery, current
membership/role safeguards, genuine Worker events, two-client reconnect,
keyboard/focus and mobile assertions retain their deadlines and retry policy.
Three omission mutations fail against the previous verifier (66 pass / three
fail); all 69 guards pass after strengthening. Browser TypeScript and workflow
syntax pass; 112 named steps and seven immutable matrix executions remain.

The complete five-case local strict permission invocation is running, not a
confirmed pass. Current immutable/full CI, remaining operation/lifecycle/role
inventory and the full PRD acceptance still govern closure. Estimated PRD-05
work remaining stays **15%** (planning estimate).

## Complete strict native permission execution

All five visibility/member/live-administrator cases pass in one 3.5-minute
strict invocation, with zero skips/retries/flaky cases. Both viewport member
consent/conflict/removal and visibility/public read-only scenarios, plus the
two-administrator genuine live update/reconnect case retain their original
assertions, deadlines and retry policy. Runtime: current compiled strict
Production API/separate Worker, rebuilt archive-focus web assets and restricted
schema-114 PostgreSQL 17/pgvector. Owned containers/database were removed.

This completes that five-case scoped local invocation. It predates the subsequent
copy-dialog exit-focus change and is not a combined 28-case or current immutable
release result. A new complete 28-case phase is running against the new web;
current immutable/full CI, remaining permission inventory and full acceptance
remain required. Estimated PRD-05 work remaining stays **15%**.

## Full-browser private fixture dependency repair

The [matrix fixture ownership repair](integration-ci-groups.md#private-browser-fixture-producers-remain-local-to-each-job)
restores preparation of the Board member directory and Board invitation-link
fixtures in each isolated full-browser job. Their original commands-only jobs
cannot provide private files or matching database rows across that boundary.
Existing complete native consumers remain mandatory; original checks are not
skipped or weakened. All 75 workflow guards pass, including six regressions for
missing/late private fixture producers. Current repaired immutable/full browser
execution remains pending. Estimated PRD-05 work remaining stays **15%**.


## Preserve chosen member confirmation during dialog entry

The completed combined strict Board phase passed 26 of 28 cases and failed
phone activity opening and phone member consent. A real-MUI component
regression independently reproduces an opening-transition defect: choosing
Confirm before entry finishes loses focus to Cancel when `onEntered` runs
(14 existing tests pass / one new regression fails before repair). This is
confirmed component behavior; the native phone failure's exact cause is not
established by that regression alone.

Cancel receives initial focus. Delayed entry recovery now checks the dialog's
own paper/trap fallback before focusing Cancel, preserving a chosen control.
Closing recovery, reviewed versions, command consent, uncertain-write warnings,
authority checks and live recovery remain unchanged. All 18 member/focus
component checks, web TypeScript, targeted lint and a fresh Production web
build pass. The unchanged two-viewport strict member scenarios are running
against the rebuilt assets; native and current immutable/full CI acceptance
remain pending. Estimated PRD-05 work remaining stays **15%**.
