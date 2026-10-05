# PRD-15 acceptance map — open

Scope is the complete issue #16 specification. This map does not close the
issue or replace its dependencies (PRD-08, PRD-17 and PRD-24). Source tests,
restricted database contracts, exact-image HTTP/Worker checks and native UI
checks establish different claims; none substitutes for all the others.

## Current native recovery investigation

At `e77195d`, run [37241937689](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37241937689) completed with 129 native passes, 15 failures and one skipped case. The desktop comment scenario passed; the phone scenario did not acknowledge its original retry. The retained diagnostic archive has SHA-256 `889994bbda2f0bc36868ce2740949547f49fb9e857d46a7ec164ddb79a05be48`. Its phone trace contains the initial substituted 503 and underlying committed 200, followed by a retry identity preflight and overlapping/aborted Board reads without a second comment POST. The original unconfirmed intent remains visible rather than being discarded.

BoardScreen now retains its admission-checking state across queued refreshes instead of briefly enabling controls between reads; List/Card creation controls also wait for current admission. Its Board workspace reports that state with `aria-busy`. The native comment fixture waits for actual Worker delivery, the rendered changed-Card review and a non-busy Board workspace before activating the original retry. Existing original body/key, no duplicate comment, two-client plaintext/edit/redaction, focus and accessibility assertions remain required. All 36 BoardScreen component cases, web/browser typechecks, lint, production build and both native scenario collection checks pass locally. The new component regression observes DOM disabled-state transitions across two controlled reads and current edit-access withdrawal; it proves no intervening enabled creation control. Current immutable-image native execution remains required before treating this repair as full acceptance. PRD-15 remains open, with an estimated 40% remaining.

## Functional requirements

| Requirement | Implementation / scoped evidence | Remaining verification or dependency |
| --- | --- | --- |
| FR-001 comment participation | `CardCommentService`, POST endpoint, guarded PostgreSQL/Demo stores, MUI author commands; managed HTTP and exact comment command fixture | Current immutable-image native author workflow and complete release gate |
| FR-002 author/content/timestamps/edited state | Stable author identity, created/updated/edited timestamps, revisioned content; SQL ownership/revision guard and MUI plaintext/edited state | Current native rendering and lifecycle gate |
| FR-003/004 own edit/delete | Current participation/author checks, dual Card/comment revisions, confirmed redaction, body-free receipts, protected original acknowledgment hydration | Full current native conflict/retry/redaction and parent lifecycle matrix |
| FR-005 username mentions | Canonical reserved handles, explicit selected identity/revision, bounded current teammate lookup, immutable revision snapshots, MUI picker | Current native selected-handle/identity recovery and cross-feature account lifecycle |
| FR-006 groups | Explicit @card/@board consent, current assignment/Board recipients, elevated Board administration, rolling three-delivery/ten-minute quota, atomic rollback | Current native group consent/quota/role gate and large recipient behavior |
| FR-007 notifications | Immutable source identities, distinct non-self deltas, current recipient admission, atomic source/inbox/jobs/receipts; managed and exact command/Worker fixtures | Current native private inbox and full PRD-17 consumer/lifecycle requirements |
| FR-008/009 immutable activity/envelope | Append-only Work journal, stable IDs/typed targets/versions, captured actor label, body-free metadata, historical Board coordinates, migrations 062–065; restricted SQL contracts | Complete producer/event coverage as remaining domain features are implemented; full release gate |
| FR-010 historical actor | SQL caption immutability through rename/deactivation/reused identity, exact API rename fixture, plaintext safe rendering | New actual HTTP legal teammate deactivation and native historical caption case must execute |
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

The issue remains open. Current estimated remaining work is **40%**, covering
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
