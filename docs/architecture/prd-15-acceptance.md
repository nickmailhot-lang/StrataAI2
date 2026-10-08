# PRD-15 acceptance map — open

Current status (2026-10-08): open, **36% estimated work remaining** (planning estimate). Earlier estimates below record their evidence scope.

Scope is the complete issue #16 specification. This map does not close the
issue or replace its dependencies (PRD-08, PRD-17 and PRD-24). Source tests,
restricted database contracts, exact-image HTTP/Worker checks and native UI
checks establish different claims; none substitutes for all the others.

## Executed local native comments and activity

Both complete desktop/phone comment scenarios pass against the local Production
API, restricted schema-110 PostgreSQL and current Vite source with disposable
Workers scoped only to the new fixture Organizations. Actual lost-response recovery
requires identical original key/body, both clean dialogs recover a changed account
timezone automatically, stored history stays unchanged, offline edit recovery and
confirmed redaction succeed, the old body receipt is refused, exactly three comment
events remain and the automated WCAG-tagged scan passes. The invocation exits 0
with two cases in 59.1 seconds.

The first full run exposed a transient-read recovery defect: clearing rows removed
the displayed Card version required by the recovery guard, preventing further
reads after reconnect. Recovery now uses fresh protected admission when rows are
cleared, retains a continuation only at the same current Card version and starts
the first page after a changed version. Actual denial still retires background
reads. Both regression cases and all 63 combined comment/activity/star/identity
component cases pass; web typechecking/lint pass.

Both complete desktop/phone activity scenarios also pass: real Board/Card paging,
automatic account-timezone recovery with unchanged UTC source timestamps,
body-free two-client updates/reconnect, revoked access, archived/deleted history,
historical labels after legal teammate rename/deactivation, keyboard and automated
WCAG-tagged checks. Terminal exit 0, two cases in 2.6 minutes. Workers retire after
each invocation. Timings are not performance benchmarks. These local immutable
API/Worker builds and Vite source are not current retained-image evidence; full
release, mass-mention, remaining producers and cross-feature acceptance are still
required. The current planning estimate is **38% work remaining**.

## Production-bundle activity preference verification

At `62d589ee`, both complete desktop/phone activity scenarios additionally pass
with a frozen production web bundle, compiled Production API, restricted schema-110
PostgreSQL and separate scoped Worker. Actual other-client timezone recovery,
keyboard paging, two-client delivery/reconnect, revoked disclosure, archived/deleted
history, immutable historical labels, automated WCAG-tagged scanning and overflow
checks pass. The initial fixture origin mismatch was corrected before these passes;
the combined invocation still failed its unrelated desktop star case, which was
subsequently repaired and verified separately. See the [execution boundary](prd-02-acceptance.md#executed-history-preference-recovery-and-star-focus-repair).
This is not current retained-image or full PRD-15 acceptance. Estimated work
remaining stays **38%**, a planning estimate; the issue remains open.

## Earlier retained-release recovery investigation

At `e77195d`, run [37241937689](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37241937689) completed with 129 native passes, 15 failures and one skipped case. The desktop comment scenario passed; the phone scenario did not acknowledge its original retry. The retained diagnostic archive has SHA-256 `889994bbda2f0bc36868ce2740949547f49fb9e857d46a7ec164ddb79a05be48`. Its phone trace contains the initial substituted 503 and underlying committed 200, followed by a retry identity preflight and overlapping/aborted Board reads without a second comment POST. The original unconfirmed intent remains visible rather than being discarded.

BoardScreen now retains its admission-checking state across queued refreshes instead of briefly enabling controls between reads; List/Card creation controls also wait for current admission. Its Board workspace reports that state with `aria-busy`. The native comment fixture waits for actual Worker delivery, the rendered changed-Card review and a non-busy Board workspace before activating the original retry. Existing original body/key, no duplicate comment, two-client plaintext/edit/redaction, focus and accessibility assertions remain required. All 36 BoardScreen component cases, web/browser typechecks, lint, production build and both native scenario collection checks pass locally. The new component regression observes DOM disabled-state transitions across two controlled reads and current edit-access withdrawal; it proves no intervening enabled creation control. Current immutable-image native execution remains required before treating this repair as full acceptance. PRD-15 remains open, with an estimated 40% remaining.

## Functional requirements

| Requirement | Implementation / scoped evidence | Remaining verification or dependency |
| --- | --- | --- |
| FR-001 comment participation | `CardCommentService`, POST endpoint, guarded PostgreSQL/Demo stores, MUI author commands; managed HTTP and exact comment command fixture | Current immutable-image native author workflow and complete release gate |
| FR-002 author/content/timestamps/edited state | Stable author identity, created/updated/edited timestamps, revisioned content; SQL ownership/revision guard and MUI plaintext/edited state | Current native rendering and lifecycle gate |
| FR-003/004 own edit/delete | Current participation/author checks, dual Card/comment revisions, confirmed redaction, body-free receipts, protected original acknowledgment hydration | Full current native conflict/retry/redaction and parent lifecycle matrix |
| FR-005 username mentions | Canonical reserved handles, explicit selected identity/revision, bounded current teammate lookup, immutable revision snapshots, MUI picker | Current immutable-image selected-handle/identity recovery and cross-feature account lifecycle; local execution is recorded below |
| FR-006 groups | Explicit @card/@board consent, current assignment/Board recipients, elevated Board administration, rolling three-delivery/ten-minute quota, atomic rollback | Current immutable-image group consent/quota/role gate and large recipient behavior; local execution is recorded below |
| FR-007 notifications | Immutable source identities, distinct non-self deltas, current recipient admission, atomic source/inbox/jobs/receipts; managed and exact command/Worker fixtures | Selected-mention native private inbox/live delivery now executed below; current-image and full PRD-17 consumer/lifecycle requirements remain |
| FR-008/009 immutable activity/envelope | Append-only Work journal, stable IDs/typed targets/versions, captured actor label, body-free metadata, historical Board coordinates, migrations 062–065; restricted SQL contracts | Complete producer/event coverage as remaining domain features are implemented; full release gate |
| FR-010 historical actor | SQL caption immutability through rename/deactivation/reused identity, exact API rename fixture, plaintext safe rendering | Current immutable-image historical caption/deactivation and complete actor-lifecycle acceptance; executed local caption cases are linked above |
| FR-011 paginated Board/Card views | Visibility-before-limit 51-source window, 50-row pages, complete eligible historical Board lookup, opaque viewer/target-bound expiring cursor, persistent API keys, MUI bounded pages | Current native paging/reconnect/access and remaining move/archive detail dependencies |

## Acceptance criteria and linked scenarios

| Criterion / test | Evidence scope | Remaining gap |
| --- | --- | --- |
| AC-15-01 / TC-01 primary action | Real HTTP comment commands/revisions/receipts, actual restricted persistence and MUI author controls | Full current native authoritative-result proof |
| AC-15-02 / TC-03/04 invalid/unauthorized | Stable errors, rejected effects unchanged, author/participant/tenant and current role checks; whole-batch telemetry privacy tests | Complete cross-feature/current native gate |
| AC-15-03 / TC-08/09 two clients/reconnect | Worker/SignalR invalidation and clean-view reload; native comments/activity scenarios use two issuing sessions, actual commands and interrupted live transport | Execute complete native suite; synthetic historical source setup is not Card movement |
| TC-02 empty state | Source component tests and real empty comment browser path | Current native execution |
| TC-05 access loss | Source UI clears protected state; exact-image source/current Board, account/session revocation and natural expiry during observed lock waits passed | Current native access-loss proof and remaining parent/producer lifecycle |
| TC-06/07 timeout/retry | Real committed request with lost reply in browser fixture, original body/key recovery, scope/version revalidation and no duplicate effects in runtime fixture | Current native run and new supported-size real command/retry fixture |
| TC-10 archive/delete | HTTP frozen writes/read-only archive, current elevated body-free deleted target history, role-revoked receipt refusal; separately admitted MUI archived Card/List reader and native scenarios now exist | Archived detail API tests have passed; cross-Board producer and new command tests now exist, with exact-image/native execution still required |
| TC-11/12 keyboard/mobile | MUI names, consent, focus recovery/no focus stealing, desktop/390px native scenarios; Axe and viewport checks exist | Current complete native WCAG/keyboard/mobile execution |
| TC-13 large data | Actual supported Board/archive activity fixture passed with 100,000 sources and retained timings; comment fixture adds bounded first/seek/final redaction plus real commands | Execute comment fixture and its p95 <500ms/recovery/publication assertions; browser capacity remains separate |

## Evidence ledger and limits

- `13865bf`, run 37187388271: exact-image activity step 37 passed historical
  tied seek, cursor binding/restart, immutable actor rename, source/current Board
  post-wait denial, issuing-session revocation and natural expiry. Native/full
  release remains live; this is not real cross-Board movement evidence.
- `080f448`, run 37187706476: supported-size activity step 41 passed, artifact
  11297303825 inspected. Twenty samples gave Board read p95 1,015.804 ms and
  Card read p95 994.213 ms. These are activity-read measurements, not mutation,
  cached detail or browser rendering budgets.
- `2b49be4`, run 37188015571: managed five-year controlled-clock history/session/
  cursor scenario passed. It is actual HTTP/Demo execution, not five deployed
  years or a new configurable purge/erasure policy.
- `6574058`, run 37188968317: source gates passed; exact-image operator steps
  42–44 passed Collector configuration, actual activity observation ingestion/
  private-field exclusion and retained evidence. This revision predates comment
  observation extension and comment capacity/mutation checks.
- `54cf6bd`, run 37189695461: all source jobs passed the comment/mention
  observation extension and focus recovery regression. Its new Collector/native
  runtime claims still require the corresponding release steps.

The shared normal Kanban benchmark preserves <1,500ms usable Board rendering,
<200ms cached detail, <100ms movement feedback and <500ms mutation p95, with
fixed normal conditions and retained measurements (`docs/kanban-performance.md`).
Historical green evidence does not establish the current feature set. Comment
capacity now gates real command p95 separately, and its execution is pending.

Telemetry is aggregate, fixed-category and separate from immutable audit;
correlation/errors, read success/denial, feature use, retry/conflict/reconnect and
client exceptions retain no protected body/identity/cursor/key material. Existing
and new tests must execute at their stated scopes before final closure. Retention
keeps append-only body-free sources without an automatic age purge, while all
reads retain current admission. Future cross-PRD erasure policy requires explicit
compatible rules, not silent source deletion.

## Producer gaps that cannot be waived

The authorized move command now supports a destination in a different Board
within the same Organization, with original-source receipt admission and both
Board stream events. Revision `73812de` passed Linux managed/API and PostgreSQL
checks, including real HTTP movement, label/assignment policy and personal
Reminder owner checks. Exact-image release and native movement execution remain
required. New Checklist, URL attachment, comment, Watch and historical inbox movement
cases now exist; their execution, file/cover consumers and complete native
acceptance remain required.

`BoardScreen` now falls back to an independently admitted archived detail reader
when a Card is absent from the active canvas. It supports archived Cards and
active Cards in archived Lists, with read-only title/description, comments and
activity. The archive directory links to this detail. Scoped admission uses the
existing authenticated read transaction, current Organization/Board access and
fresh Card/List checks under the Board gate; deleted content remains unavailable.
Archived lifecycle API cases passed Linux CI at `8b9623e`; focused component tests
also passed locally. Full exact-image desktop/mobile execution remains required. Deleted target history
stays body-free/current-admin-only; this gap does not authorize deleted body
disclosure or an anonymous/Owner Portal activity projection.

The issue remains open. Current estimated remaining work is **38%**, covering
these producer/UI dependencies, full current native/runtime proof, remaining
capacity/performance and cross-feature acceptance rather than only the recently
implemented readers.


Cross-Board producer continuation adds both Board stream events, stable Card
routing and original-source receipt admission. New HTTP tests cover current
history after movement; an exact-image rollback/retry fixture is mandatory.
The producer and new HTTP cases passed Linux managed/API execution; full release
and native acceptance remain pending. PRD-15 remains open with approximately 40% remaining.
# Activity denial fixture synchronization

Web job 111444596527 in run 37205119205 failed the 403 keyboard-denial case:
the initial page's rows could render before its passive focus recovery completed,
and the fixture immediately started another operation. The fixture now waits for
initial Older activity focus before testing denial. All denial, no-extra-read,
private-content clearing and Close activity focus assertions remain mandatory.
All 11 focused activity-history tests pass locally. Production behavior is
unchanged; full Linux web execution remains pending.

## Executed selected and group mention recovery

All six cases in `account-mention-handle.spec.ts`, `comment-mentions.spec.ts` and
`comment-mass-mentions.spec.ts` passed in one complete local invocation on
2026-10-08, covering 1280px desktop and 390px phone. The topology uses the current
MUI production bundle, compiled Production API, separate explicitly scoped
compiled Worker, restricted PostgreSQL 17/pgvector schema 112 and Nginx.

The first invocation had five passes and a phone group failure: an asynchronous
foreground Board read began during account preflight, so the client refused the
next new mutation before sending a comment POST. Waiting for actual workspace
admission repaired that activation. A second invocation passed both group cases
but the phone selected-mention retry keypress landed during a focus/read
transition; its trace contained the stale-handle rejection and committed original
response, but no second retry POST. Both fixtures now use the existing focused
admission helper for save/original retry. It retries focus checks, activates once
and never repeats a rejected mutation. Authorization, preflight guards, consent,
quotas and exact original key/body/effect assertions remain unchanged. Both failed
invocations are retained as diagnostics; neither counts as full acceptance.

The final complete invocation exits 0 with all six passes. Selected mentions
reject a reviewed handle revision even after the teammate reclaims the same
handle, leaving comments and recipient inbox empty. Fresh selection creates one
recipient notification; a deliberately substituted failure after real commitment
recovers the identical original key/body and selection revision. The author inbox
stays empty, and Board membership removal hides the recipient notification.

Confirmed overlapping `@card`/`@board` groups deduplicate one recipient. Plain
unconfirmed `@board` text creates a comment without another delivery. Two later
confirmed groups produce three deliveries total; the fourth is refused by the
rolling three-per-ten-minute quota, with four persisted comments at Card version
6 and no unresolved retry. Ordinary members cannot confirm `@board`; their own
confirmed `@card` action creates no self notification. Grant removal withdraws
inbox disclosure. Native account-handle cases additionally prove original
acknowledgment recovery, retained unsaved profile edits, concurrent account CAS,
new-key review after conflict, old-handle receipt refusal and logout withdrawal.
Keyboard, focus, scoped Axe and phone overflow assertions pass unchanged.

These mention cases inspect recipient notifications through real authorized HTTP
inboxes; they do not independently prove recipient MUI or mention WebSocket-frame
delivery. Account policy permits unverified fixture accounts, matching CI's
browser phase. Email-provider verification and strict policy remain separate.
Assemblies are mounted read-only in cached framework containers; this is local
compiled evidence, not current retained-image identity or a capacity benchmark.
All owned containers and cloned databases are removed, preserving existing
services/volumes. Browser typechecking and diff checks pass. The full CI browser
suite includes the corrected fixtures. Complete current-image, producer,
concurrency/capacity and Definition of Done acceptance still govern closure.
Estimated PRD-15 work remaining is **36%**; PRD-17 remains **21%**.

## Executed recipient mention inbox and private live delivery

Both selected-mention cases in `comment-mentions.spec.ts` passed together on
2026-10-08 at 1280px and 390px (2 passed, exit 0). This extends the selected
mention evidence above to the actual recipient MUI inbox and SignalR frames.
The group cases above still inspect recipient HTTP inboxes only.

Each recipient opens the native inbox before the author command and receives
its initial private live snapshot. Rejection of a reclaimed but stale selected
handle leaves comments, native inbox and notification events empty. After a
fresh selection, real commitment followed by a substituted failed response and
identical original-key/body retry produces exactly one `NOTIFICATION_CREATED`.
Every observed snapshot/event matches the Organization and recipient; events
identify Notification entities and expose empty metadata. The native inbox
shows one unread mention with the canonical Card link. Keyboard mark-read
produces exactly one `NOTIFICATION_READ` and a persisted read timestamp.
The author inbox remains empty. Actual Board membership removal clears the
recipient's HTTP inbox and native article without another creation/read event.
Tagged Axe and horizontal overflow checks pass on both recipient views.

The invocation uses the current MUI production bundle, read-only compiled
Production API and separate scoped Worker with PostgreSQL 17/pgvector schema
112 and Nginx. Its fixture-account policy permits unverified accounts, matching
CI's browser phase. This does not prove strict email verification or current
immutable release-image acceptance. All invocation-owned containers and the
cloned database were removed; existing services and data were preserved.
Browser typechecking and diff checks pass. The two cases remain mandatory in
the full CI browser suite. Current-image execution, group-recipient native
coverage, remaining producer/concurrency/capacity and full Definition of Done
requirements still govern closure. PRD-15 remains open with **36% estimated
work remaining**; PRD-17 remains open with **21% estimated work remaining**.

## Executed confirmed-group recipient inbox and live delivery

Both complete `comment-mass-mentions.spec.ts` cases passed together at 1280px
and 390px on 2026-10-08 (2 passed, exit 0). The actual recipient MUI inbox and
private SignalR subscription are open before the comment commands. The initial
real Card assignment is identified independently, so its notification/live
history cannot be mistaken for a mention delivery.

Confirmed overlapping Card/Board groups plus original-key/body acknowledgment
recovery yield one mention article and one private creation event. Unconfirmed
group text causes no additional delivery. Two further confirmed groups produce
three mention articles and exactly three `NOTIFICATION_CREATED` events, alongside
one original assignment article. Every observed snapshot/event matches the
Organization and recipient and contains Notification entities with empty
metadata. All four native links target the canonical Card. The fourth group is
rate-limited with four comments at Card version 6; ordinary member Board-group
consent stays disabled and their own confirmed Card group adds no notification.
Actual Board membership removal clears the HTTP inbox and all native articles,
with no extra creation/read events. Recipient tagged Axe/overflow assertions pass.

The first invocation passed desktop but failed phone acknowledgment of the
unconfirmed comment. Its private trace contains only the overlapping-group
original/retry POSTs, with no unconfirmed-comment POST; the draft/Save control
remained present. The fixture now brings the author page to the foreground
before waiting for actual workspace admission and activating one focused save
keypress. It retries neither commands nor authorization failures. The subsequent
complete invocation passes both cases without changing consent, permissions,
quota, response-loss or exact-effect assertions. Failed evidence is retained.

The topology matches the compiled-runtime selected-mention invocation above;
fixture accounts permit unverified email. All owned containers/database were
removed, preserving existing services/data. Browser typechecking and diff checks
pass. This completes local native recipient UI/live checks for both selected and
group mentions, superseding their earlier HTTP-only limits. Current immutable
build-once images, remaining producer/concurrency/capacity, strict-policy and
full Definition of Done acceptance still govern closure. Estimated PRD-15 work
remaining stays **36%**; PRD-17 stays **21%** (planning estimates).

## Complete persisted native mention attribution

The [final producer-family checks](prd-17-acceptance.md#executed-complete-attribution-across-notification-producer-families)
pass selected mentions at both widths and the two repaired group cases together
in 1.8 minutes. Every actual mention matches its persisted Card source actor,
identity/revision, Board, source type and creation clock. Complete private creation
and selected-mention read envelopes match all stored journal fields and full
significant creation/first-read precision. Group consent, overlap deduplication,
unconfirmed-text suppression, quota refusal, member/self scope, original receipt,
automatic retry focus, current withdrawal and tagged Axe/overflow stay asserted.
The failed final group invocation is retained; its read-only draft opener now
foregrounds the author and establishes enabled/admitted focus before one keypress.
API fixture verification remains optional. Current immutable images, strict policy,
remaining producer/concurrency/capacity and full Definition of Done still govern
closure. PRD-15 remains open at **36% estimated work remaining**; PRD-17 **15%**.

## Strict verified-account native selected and group mentions

The [complete strict producer invocation](prd-17-acceptance.md#executed-strict-verified-account-notification-producer-families)
passes both selected-mention widths and both confirmed-group widths, alongside
assignment and real reminder firing, in one eight-pass 8.2-minute run. Authors
and recipients must fail login before verification and succeed afterward; actual
API/Worker policy remains strict. Original selected revision, group consent/quota,
recovery focus/receipt, self/role scope, withdrawal, full persisted envelopes/clocks
and tagged accessibility assertions pass unchanged. The required build-once strict
phase now includes all four mention cases. CI-only fresh-account activation when
the token is private is not email-provider proof. Local strict admission now
supersedes the optional-policy limit for these cases; current immutable/full CI,
remaining producer/concurrency/capacity and Definition of Done still govern closure.
PRD-15 remains open at **36% estimated work remaining**; PRD-17 **15%**.

## Command confirmation through automatic recovery

A fresh current-source native group-mention invocation received successful
comment responses but failed to observe `Comment added.` on a later submission.
Automatic Card invalidation/reconnect used the explicit-review load path, which
cleared the accepted command's announcement. The new PRD-15-TC-08/09 component
regression fails before repair on that missing confirmation.

Automatic rereads now use the existing recovery path. The confirmation survives
Card-version invalidation and reconnect; an explicit review clears it. The
regression independently requires the authoritative rereads and exactly one
write. Current actor/read admission, dirty drafts and uncertain originals retain
their existing guards. All 34 selected comment/focus cases, web/browser types,
targeted lint and the Production web build pass.

The final strict native producer invocation passes all eight assignment,
selected-mention, confirmed-group and actual due-reminder cases in 8.2 minutes,
at desktop and phone widths, with no skips or retries. It uses rebuilt current
MUI assets, current compiled Production API/separate Worker and restricted
PostgreSQL 17/pgvector schema 114. Both account policies require verified email;
the existing CI fixture activates only newly registered disposable accounts
when tokens are private, which does not prove provider delivery. Full stored
notification attribution, audit clocks, private transport, receipt/focus recovery,
group quota/consent, withdrawal and accessibility assertions remain.

Earlier invocations reported retry-focus failures; their diagnostics are retained
and the final run keeps those assertions. Their intermittent cause is not
established by the confirmation regression. Temporary diagnostic edits and owned
containers/database are removed. Current immutable/full CI and remaining complete
acceptance/DoD still govern closure. PRD-15 remains at **36% estimated work
remaining** and PRD-17 at **15%** (planning estimates).
