# Navigation observations — implementation in progress

FOUND-FR-009's mutable-record audit now includes the
[executed session/security-token lifecycle clock repair](architecture/identity-lifecycle-clocks.md).
That scoped upgrade and restricted-store proof does not certify every mutable
entity or full PRD-01 acceptance. Estimated work remaining stays **34%**.
The [canonical invitation metadata audit](architecture/invitation-audit-metadata.md)
also projects its existing stored clock/revision through creation and lifecycle
reads, with complete restricted returned-row and recovery checks.

PRD-01 requires `APPLICATION_CONTEXT_CHANGED`, `BOARD_OPENED`, and `CARD_OPENED`. These are personal navigation observations. They do not change shared Board/Card content or replace audit history. See the [documentation index](README.md), [routing isolation](architecture/routing-isolation.md), and [current actor sessions](architecture/command-actor-sessions.md).

## Implemented behavior

The admitted internal Organization shell confirms Organization context. The authorized Organizations directory confirms global context after its account-bound read completes. Active Board and Card screens confirm opens after their current reads finish. Anonymous visitors submit no personal observation. Archived Boards, archived Lists, and unavailable targets cannot authorize an active-target observation.

`POST /navigation/observations` accepts an empty body and exactly one value per supported query field:

| Kind | Query fields |
| --- | --- |
| `context` | `kind=context`; optional `organizationId` |
| `board` | `kind=board`, `organizationId`, `boardId`, positive `version` |
| `card` | `kind=card`, `organizationId`, `boardId`, `cardId`, positive `version` |

The request requires authentication, the normal application request marker, an `Idempotency-Key`, and `X-StrataAI-Expected-Actor`. The server validates the current session, target access, parent lifecycle, and observed revision inside the owning transaction. Replays require current authorization; a receipt is not an access grant. An exact persisted original remains recoverable after later entity revisions; fresh observations still require the current revision. Current target scope and active parent lifecycle are rechecked in both cases. Responses are private and must not be cached.

The canonical acknowledgment has exactly ten fields: `eventId`, `eventType`, `actorId`, `organizationId`, `boardId`, `entityType`, `entityId`, `version`, `metadata`, and `createdAt`. Metadata is empty. Global context uses `ApplicationContext` with its event ID as entity ID; Organization context uses the Organization ID. Board and Card observations use their actual entity IDs and revisions. Timestamps preserve PostgreSQL microsecond precision.

## Retry and consumption

The browser checks the account before and after dispatch, validates the acknowledgment against the independently admitted target, and deduplicates original event IDs in a bounded account-specific window. Account replacement or screen cancellation prevents consumption.

Before dispatch, the original request is retained in session storage under its account and target. Returning to the same target recovers the original key and revision, even if the newly loaded revision differs. Successful confirmation removes only the matching retained key. A lost response offers **Retry navigation confirmation**; editing the entity does not replace the unresolved open request.

The retry window is 24 hours. Browser recovery storage allows 1,000 retained requests and reclaims at most 100 canonical expired originals owned by the current account when full. Live or foreign-account originals are not evicted. Storage failure prevents dispatch of an unretained new original. Session storage is local to the browser tab; it is not a cross-device event queue.

Production sources and receipts use PostgreSQL forced RLS with actor-private access and immutable originals. Receipt admission serializes through the active actor, retains the original event ID/time, and performs current target authorization on replay. The separate Worker has no navigation-source grant. These records are separate from optional analytics and shared work events.

## Verification and remaining work

Navigation confirmation reports optional client observations through the existing authenticated `/me/activity-client-events` endpoint. Fixed actions are `navigation_context`, `navigation_board`, and `navigation_card`; fixed kinds record visit opens, attempts, explicit user retries, exceptions, and success/failure timing. A visit open is counted once, while each admitted attempt is counted separately. Cancellation does not report a failure or exception. Timing covers account checks, admission, transport and recovery completion, and is not the Board rendering or server mutation performance measurement.

The bounded best-effort queue contains only action, kind, count and optional duration. It retains no actor, entity, Organization, route, request key, original event, content or exception detail. Reports are never retried; report failure cannot change the authoritative acknowledgment. These untrusted measurements are separate from immutable navigation sources and shared audit history. Server parsing rejects extra fields. Stable permission-denial and realtime measurements still require their separate acceptance evidence.

Focused model, producer, HTTP, browser consumer, transport, component, and recovery fixtures exist. Local browser tests cover canonical validation, account replacement, cancellation, lost responses, return visits, and bounded recovery storage. Local .NET compilation does not prove runtime acceptance; native SQL and HTTP execution must pass CI for the relevant revision.

Broader native replay expiry/capacity/concurrency evidence and full PRD-01 acceptance remain incomplete. Full-suite verification must follow fixture repairs; focused recovery success alone does not establish a full-suite result. Exact-image browser, performance, accessibility, lifecycle, and realtime acceptance must also be verified before closing the ticket. Navigation observations do not by themselves prove the PRD's telemetry or performance requirements.

### Acceptance evidence map

The following fixtures cover navigation-specific requirements from [PRD-01](https://github.com/nickmailhot-lang/StrataAI2/issues/1). A fixture's existence is not a passing result. Check its execution for the same application revision before using it as acceptance evidence.

| Requirement or scenario | Executable evidence | Verification boundary |
| --- | --- | --- |
| Canonical fields, actor and target admission, immutable originals | [Producer tests](../tests/StrataAI.Api.Tests/NavigationInteractionProducerTests.cs), [store tests](../tests/StrataAI.Api.Tests/NavigationInteractionStoreTests.cs), [browser acknowledgment tests](../apps/web/src/app/navigationInteraction.test.ts) | Browser checks execute locally; .NET runtime execution requires CI on this workstation. |
| Invalid input, anonymous or wrong actor, lost response, duplicate submission, access revocation | [HTTP tests](../tests/StrataAI.Api.Tests/NavigationInteractionHttpTests.cs), [exact-image HTTP checks](../scripts/ci/test-navigation-observations.sh) | Demo host tests and production container checks are distinct requirements. |
| Forced RLS, private receipts, expired receipt reclamation, capacity, rollback, recovery after revision changes | [Restricted PostgreSQL fixture](../scripts/ci/test-navigation-interaction-sources.sql) | Requires the native PostgreSQL CI job; compilation cannot verify these guarantees. |
| Concurrent requests with the same original key | [Exact-image HTTP checks](../scripts/ci/test-navigation-observations.sh) | All concurrent responses must contain the same persisted original, through the release proxy. |
| Retained original, return visit, account isolation, expiry and bounded browser storage | [Recovery tests](../apps/web/src/app/navigationRecovery.test.ts) | Local storage tests do not prove server admission or native browser behavior. |
| Desktop, tablet, phone, keyboard Back, lost response followed by an entity edit, accessibility | [Native browser checks](../tests/browser/navigation-observations.spec.ts) | Runs at 1280, 768 and 390 pixels against release images; execution remains pending for the current implementation. |

Full foundation acceptance also requires hierarchy integrity, all mutable-record audit fields, lifecycle-aware deep links, preserved Board viewport context, authorized two-client updates and reconnect recovery, and the stated capacity and performance targets. Those requirements span other feature suites and their acceptance records; this navigation evidence map does not establish their completion.

### Revision-specific verification ledger

- CI run [37407613926](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37407613926)
  at `e3bcb1d` passed web quality: 122 files and 1,468 tests, followed by the web
  build. Its API-host job failed the two navigation HTTP scenarios with
  `BadRequest`; the navigation route was absent from the idempotency middleware
  at that revision. `5caf8be` subsequently added matched-route handling and
  invalid-key/trailing-slash fixtures; `74ce75d` added private/no-store headers
  before middleware key rejection. Current runtime verification is pending.
- The same run's PostgreSQL job rejected a complete migration ledger before
  restricted search traversal. `e158969` removed the stale numeric readiness
  count, and `e0c78cd` added restricted API/Worker complete, missing and restored
  ledger checks. See [schema readiness](architecture/schema-upgrades.md#runtime-migration-readiness).
- Local full web execution at `ca81d3f` finished with 121 files passing and one
  failing: 1,468 tests passed and one URL-attachment recovery fixture failed.
  `c6541ed` supplied its canonical navigation acknowledgment and waited for
  enabled command admission and actual original dispatch. The focused repaired
  scenario and typecheck passed. A fresh full run is pending; no current-main
  full-suite or release success is asserted by these results.

These results identify separate source, HTTP and persistence failures and their
later repairs. They do not prove exact-image browser, performance, accessibility
or whole-ticket acceptance.

- Subsequent CI run [37408240323](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37408240323)
  at `74ce75d` passed .NET quality: 643 Domain tests and 426 API-host tests,
  with zero failures. This provides native execution evidence for the navigation
  HTTP middleware/key/cache repairs present at that revision. Web quality also
  passed. PostgreSQL failed with the same runtime schema readiness exception
  before restricted search traversal; that revision predates `e158969`.
  Whole-release and current-main acceptance remain unproven.

## Observation transaction admission

Navigation originals and retries use the dedicated identity observation boundary.
Production locks the scoped Organization parent before shared actor admission,
then serializes private interaction history with a transaction advisory lock.
Migration 085 updates restricted append/replay functions to the same order.
This avoids the observed account/Board lock inversion while retaining current
session checks before/after storage, current target access, private subject RLS
and atomic source/receipt publication. Demo retains its account-before-Work gates.
The [CI investigation](architecture/browser-recovery-ci.md) records the failed
revision and the mandatory native lock-order regression; execution is pending.

A verified navigation visit is now retained as complete across temporary live
read admission changes. Re-admission cannot resubmit its completed original;
unresolved attempts keep their existing recovery. A new mounted visit creates a
fresh original key. The component regression covers both paths. Native execution
remains required to resolve the duplicate-key failure reported by the old
`5a2433f` browser suite.

## Native completion before leaving an acknowledged scope

Desktop, tablet and phone navigation cases pass in the current seven-case compiled
Production API/Worker/MUI/PostgreSQL invocation. A received server receipt still
requires current-account confirmation before its local original is removed.
The fixture waits for that completion before leaving an acknowledged screen;
the deliberately lost Card original survives leaving and returns with the same
key, query and event. Later visits require new keys, and final retained-original
counts remain zero. The fixture never removes storage entries. Keyboard Back,
Board context and WCAG assertions remain. See [execution evidence](architecture/browser-recovery-ci.md#archive-source-admission-and-navigation-completion-boundaries).

Estimated PRD-01 work remaining stays **34%** (planning estimate). This focused
execution does not establish complete foundation or current immutable CI acceptance.

## Card detail focus after browser history navigation

FOUND-FR-007 and PRD-01-TC-11 require predictable browser Back behavior and
keyboard focus. Card details now remember the current Card route before the
dialog exit transition runs. Browser Back returns focus to that Card's existing
Board link, using `preventScroll` to avoid moving the Board viewport. Replacing
an open detail route updates the return target. If that Card link is unavailable,
the existing Board refresh control remains the fallback. Organization/Board
remounting bounds the return target to its current scope.

Both new rendered regressions failed before the repair because focus returned
to Board refresh. The complete Board component file passed all 38 cases after
the repair. Type checks, lint and the production frontend build also passed.
The existing desktop/phone persisted Board workflow now checks Back focus,
Forward reopening and Close focus without removing its concurrent-client,
retry, deep-link or isolation assertions.

The complete web suite passes **1,986 tests across 142 files**, with no failed
or skipped cases. The complete 14-file/31-case native Board phase finished with
**30 passed, one failed**, no skipped/flaky cases and no report-level errors
(1,365.63 seconds). Both desktop/phone persisted Board workflows pass the new
Back/Forward/Close focus assertions. The desktop member-removal review failed
when a live directory refresh canceled unsubmitted consent; its phone case
passed. The failure trace remains preserved, and a complete phase rerun is
required after the pending fixture repair. This invocation is not a green
Board-phase result.
The local browser fixture uses this production frontend build with
retained API/Worker images and restricted PostgreSQL roles; it is separate from
the full immutable build-once CI gate, which remains pending. No complete
foundation acceptance or issue closure is inferred from these focused results.
Estimated PRD-01 work remaining stays **34%** (planning estimate).

### Executed large-Board history and viewport preservation

The complete rank/capacity chain now passes with all three native Chromium
viewport cases: 1280, 768 and 390 pixels. Each keeps the original 200-List and
at-least-5,000-active-Card admission checks, keyboard/pointer movement geometry,
window bounds and tagged accessibility checks. At the populated List's tail,
Back closes the Card overlay and returns focus to the same canonical Card link;
Forward reopens it, and Close returns focus again. Both exits preserve the exact
canvas, Card-window, List-section and document scroll offsets.

Each case returns its transferred Card through the real versioned move command
after its original assertions. The resulting read verifies the next revision,
restored source count, empty destination and unchanged other Cards/Lists. This
keeps every viewport above the original capacity threshold rather than letting
the added tablet case consume the shared fixture below it.

All **3/3 cases** pass in one invocation (186.60 seconds), with no skipped,
flaky or failed cases or report-level errors. The complete shell chain exits
zero after its final **100,000 archived-Card count and complete-record fingerprint**
checks. Owned containers/database and API/Worker credential files are independently
confirmed absent; browser credentials are removed and the original three
running services remain.

This uses the repaired production frontend, retained API/Worker images,
restricted PostgreSQL 17/pgvector/schema 114 and current Nginx/CSP. The rank
fixture uses optional email verification as its CI configuration does.
It is local integration evidence, separate from current immutable release CI,
physical-device coverage and the unchanged normal-condition performance budgets.
The earlier 30/31 Board phase and its member-review failure remain recorded
above. Estimated PRD-01 work remaining stays **34%** (planning estimate).

### Complete strict Board rerun after activation repairs

The fresh fourteen-file/31-case strict invocation completed with **29 passes and
two failures**, no skipped/flaky cases or report-level errors, in 1,469.35 seconds.
Both desktop/phone member-consent, metadata and Card-label cases passed. The
remaining failures were phone activity-history's final historical actor assertion
and the label-filter workflow's later desktop picker opening. Full reports and
traces remain private and were not overwritten.

The activity trace records an interrupted Board continuation followed by newest
page reads. The existing activity control resets continuation on a fresh parent
access generation; all sixteen existing component regressions pass. Its pending
browser repair readmits a reset first page within the original bounded assertion,
without repeating the account mutation or relaxing historical-name checks. The
label-filter trace records three successful assignment writes before its later
opener failure. Pending read-only opener preparation checks focused, enabled
admission before Enter; assignment/member commands remain outside opener retries.

Browser type checks and diff checks pass. Another fresh complete invocation is
running with all five pending fixture repairs and the original scenario scope,
assertions, deadlines and zero retries. Those repairs remain uncommitted pending
aggregate verification; no full passing Board result is claimed. Independent
cleanup confirms zero containers/databases from the completed failed invocation,
no API/Worker environment files and all three original services preserved.
Runtime scope remains the repaired production frontend, retained API/Worker,
restricted schema-114 PostgreSQL and current Nginx/CSP with verified accounts;
this does not prove current immutable build-once CI. PRD-01 stays open at **34%
estimated work remaining** (planning estimate).

### Subsequent complete Board result and current schema-115 rerun

The next fresh complete invocation finished with **29 passes and two failures**,
zero skipped/flaky cases or report-level errors, in 1,345.90 seconds. Both
activity-history cases passed, including the retained historical-actor assertions.
The failures were desktop Card-label filtering after Clear and label-filter
collaboration after applying due-completion criteria. Their preserved traces
record earlier successful assignment commands but no observed dispatch for the
failed filter action. Absence in a trace is not complete proof of every API path.

Pending fixture repairs now observe actual request dispatch: activation retries
stop as soon as the specific clear request or second Apply request is observed.
The selected due criterion, unchanged keyword/result assertions, exact command
counts and original scenario deadlines remain. Browser type/diff checks pass.
All five browser fixture repairs remain uncommitted pending aggregate acceptance.
Independent cleanup verifies zero owned containers/databases, removed API/Worker
environment files and preserved original services for this completed invocation.

A fresh full fourteen-file/31-case phase is active against the current locked
compiled API/Worker source through migration 115, mounted into local runtime
containers, with the repaired production frontend and verified-account policy.
Its schema-only disposable clone avoids inheriting unrelated account/job data.
This local compiled-source fixture is separate from immutable-image/build-once
release proof; its result is pending. The full scenario selection, original
assertions and zero test retries remain. PRD-01 stays open at **34% estimated work
remaining** (planning estimate).

### Complete schema-115 Board phase passed

The fresh current compiled-source invocation passes **31/31 cases across all
fourteen original files** in 1,352.49 seconds. The complete JSON report confirms
every case expected and actually passed on its only attempt: no retries, skipped,
flaky or failed cases and no report-level errors. Both history-focus workflows,
activity historical-actor recovery, member consent, metadata and Card-label
recovery, and live label/filter/assignee collaboration retain their original
scenario assertions and deadlines. Earlier failed invocations and traces remain
privately retained, rather than overwritten or represented as passing results.

Five fixture repairs admit focus/current controls before activation, re-review
unsubmitted consent after withdrawal, readmit reset history paging, or observe
actual dispatch before stopping activation retries. Destructive member consent
and label/member commands remain outside read-only opener retries. The explicit
Blue/Clear/second-Apply dispatch checks retain exact write counts, original-key
recovery, persisted state and authorization assertions. Browser type/diff checks
pass, as do all **179 integration-suite/build-metadata source checks**, no skips.

The runtime is the frozen repaired production frontend with current compiled
schema-115 API/Worker binaries mounted into local runtime containers, restricted
PostgreSQL17/pgvector, current Nginx/CSP and verified-account policy. It does not
prove immutable-image CI, newer schema-116 runtime acceptance, physical devices
or complete foundation acceptance. Independent cleanup confirms zero owned
containers/databases, removed API/Worker environment files and preserved original
services. PRD-01 stays open at **34% estimated work remaining** (planning estimate).

## Schema-116 tablet fit failures and responsive schema-117 rerun

The next intact Board phase adds a 768×1024 tablet case between desktop
1280×720 and mobile 390×844. The second authenticated client and anonymous
reader now explicitly use the same case viewport. Document-width fit checks
cover empty Organization home, Organization details, List canvas, editable Card
overlay and anonymous Board/Card reads; existing hierarchy, persistence,
lost-response recovery, two-client conflict, history-focus and isolation
assertions remain unchanged.

That complete schema-116 compiled-source invocation finished **29/32** across
fourteen files in 1,383.66 seconds, with no skips, flaky cases or report-level
errors. Tablet and mobile failed the fit check immediately after Organization
creation, before Board creation: the Organization action row did not wrap.
Desktop Card-label deletion failed while waiting for confirmation. Its trace
records no completed label DELETE network entry; that absence alone does not
prove that no command was dispatched. All failed reports/traces remain outside
the repository in `board-tablet-schema116-native-20261009`. Independent checks
confirm zero owned containers/databases, absent API/Worker credential files and
the original three services still running.

The pending product repair uses wrapping MUI flex-gap action/navigation/page
rows and breakable text on Organization home. It keeps every control available
at smaller widths. The pending deletion fixture observes actual page request
dispatch and stops activating as soon as one DELETE is sent, retaining consent,
exact single-write and confirmed persisted-deletion assertions. It does not
replay a dispatched destructive command.

All **57 tests in the two OrganizationHome/LabelManageControl component files**
pass, as do web/browser types, changed-source lint and the production frontend
build. A fresh full fourteen-file/32-case phase is active with this frozen
repaired frontend and compiled schema-117 API/Worker binaries mounted read-only
into local runtime containers. Its private directory is
`board-responsive-schema117-native-20261009`; collection confirms the complete
selection. No aggregate pass, immutable-image CI, physical-device acceptance or
issue closure is claimed. These frontend/fixture changes remain uncommitted
until full verification completes. PRD-01 remains **34% estimated work remaining**.

That intact schema-117 invocation finished **29/32** in 1,400.35 seconds,
with no skips, flaky cases or report-level errors. All three persisted hierarchy
cases pass, including tablet/mobile document-width fit, same-width second-client
and anonymous-reader checks. The desktop Card-label case passes its deletion
dispatch/confirmation checks. Failures occur in the phone Board archive retry,
phone label-management reload before reopening the updated editor, and the
secondary client's filtered-canvas activation. Private reports/traces are
retained; independent cleanup again confirms zero owned containers/databases,
removed API/Worker credential files and the original three running services.

Pending fixture repairs stop archive recovery activation when its second routed
request is observed, require the current updated Label editor after read-only
reload, and confirm local filtered-canvas state after activation. Original
two-request retry keys/bodies, child lifecycle fingerprints, label reorder and
deletion, exact filter-change counts, persisted filters and archived refusal
assertions remain. Browser types and diff checks pass. A fresh complete
fourteen-file/32-case phase is active in
`board-responsive-schema119-native-20261009`, using the same frozen repaired
frontend and current compiled schema-119 API/Worker binaries mounted read-only
into local runtime containers. Collection confirms the complete selection;
there is no narrowing or test retry. Aggregate/current immutable-image and
physical-device proof remain pending; no issue is closed.

## Complete schema-119 responsive Board phase passed

The intact fourteen-file phase finished **32/32** in **1,514.14 seconds**,
with one passing attempt per case, no skips, flaky cases or report-level errors.
Desktop, tablet and mobile hierarchy workflows pass the document-width checks
through Organization home, Board/List canvas, Card overlay and anonymous
Board/Card reads. The Organization action rows now wrap with MUI flex gaps,
and long text can break without removing controls.

Archive recovery, updated Label reload, single-dispatch Label deletion and
filtered-canvas activation pass alongside their original persisted-state,
request-count, retry-key/body, lifecycle, concurrency and isolation assertions.
The fixtures only repeat activation until the observed dispatch or local
read-only result; they retain the original recovery and destructive-operation
criteria. All original fourteen files ran without narrowing or test retries.

The private report and independent verifier are retained in
`board-responsive-schema119-native-20261009`. Independent checks confirm zero
owned containers and databases, removed API/Worker credential files and all
three original services running. This proves the frozen repaired frontend
against compiled schema-119 API/Worker binaries mounted read-only into local
runtime containers. It does not prove schema-120 runtime acceptance, current
immutable-image CI, physical-device behavior or the entire foundation matrix.
The exact schema-120 main CI run remains queued at this verification point.
PRD-01 stays open with **34% estimated work remaining** (planning estimate).

## Schema-121 aggregate result and schema-123 copy recovery verification

The subsequent intact fourteen-file/32-case schema-121 phase finished
**31/32** in **1,556.81 seconds**, with no skips, flaky cases or report-level
errors. All desktop/tablet/mobile hierarchy and viewport cases pass. The phone
Board-copy case reached lost-response recovery, focused its retry control,
then timed out waiting for the acknowledged copied Board link. Its private
trace retains an aborted frontend copy response and a successful underlying
copy fetch; those records alone do not establish whether retry dispatch occurred.
The full report and trace remain in `board-responsive-schema121-native-20261009`.
Independent checks confirm zero owned containers/databases, removed API/Worker
credential files and the original three services running.

The pending fixture repair counts POST requests at the original copy route
interceptor before fetching each response. It prepares current enabled focus
and activates the recovery control only while the first dispatch is the sole
observed request, stopping immediately at the second dispatch. The original
two acknowledgments, identical idempotency keys/bodies/returned IDs, original
source version/content, concurrent source change, keyboard focus, persisted
private copy/checklist/member/star/history, width and accessibility assertions
remain. An added assertion requires exactly two dispatched copy requests.
There is no recovery activation after that second dispatch.

Browser types and diff checks pass. A fresh complete fourteen-file/32-case
phase is active in `board-responsive-schema123-native-20261009`, with the same
frozen repaired frontend and compiled schema-123 API/Worker binaries mounted
read-only in local runtime containers. Collection confirms the entire original
selection; there are no narrowed tests or test retries. This also exercises
the newer metadata/recipient counter clocks in the browser runtime. The pending
fixture change remains uncommitted until aggregate verification completes;
current immutable-image CI and full foundation acceptance remain outstanding.

The intact schema-123 invocation subsequently finished **30/32** in
**1,407.18 seconds**, with no skips, flaky cases or report-level errors.
Both desktop and phone Board-copy recovery cases pass with the added exact
dispatch count and original replay/persistence/focus/accessibility checks.
Failures occur in activity history and Card labels; their full reports and
traces remain private. Independent checks confirm zero owned containers and
databases, removed API/Worker credential files and original services running.
The copy fixture change remains uncommitted pending complete aggregate
verification; no newer browser phase is yet started or aggregate pass claimed.

The schema-123 failure locations are now established from the private result:
desktop Card activity expected 17 older events but still observed 50 at the
first paging step; phone Label management timed out at the acknowledgment after
Move label. Trace entries do not establish that either action was dispatched.
The pending fixtures now repeat read-only paging only while Newer activity is
disabled, stopping once the older page is admitted; Label movement uses an
actual page request counter and stops activation at its first POST. It adds an
exactly-one move-dispatch assertion. The original historical labels, paging
focus, ordering, version, acknowledgment and persisted deletion checks remain.
No authorization, protected-generation or product read-admission rules change.

Browser types and diff checks pass. Collection of a fresh full invocation in
`board-responsive-schema123-admission-native-20261009` confirms all fourteen
files and 32 cases. This invocation is running with the same read-only frozen
frontend and schema-123 API/Worker build. There are no test retries or narrowed
selections, and all three pending fixture changes remain uncommitted pending
aggregate acceptance. Exact-main CI at `d085edf49d3215deab2aaa88dd2221b93f9c4962`
is queued in [run 37958606573](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37958606573).
PRD-01 remains open with **34% estimated work remaining** (planning estimate).
