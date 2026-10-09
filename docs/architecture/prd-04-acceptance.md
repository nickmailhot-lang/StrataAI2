# PRD-04 acceptance and closure audit

This audit follows the complete [PRD-04 issue](https://github.com/nickmailhot-lang/StrataAI2/issues/5), including its functional requirements, cross-cutting requirements, test scenarios and definition of done. Implementation coverage is not proof of acceptance. The ticket remains open; the current remaining-work estimate is 15%.

## Executed metadata, copy and lifecycle acceptance

Six native desktop/phone cases passed in one 4.2-minute invocation against the
Production API, restricted schema-110 PostgreSQL, separate Workers scoped only
to new test Organizations, and the fresh production web bundle behind current
Nginx/CSP. Metadata tests prove original-request recovery, genuine two-client
delivery, preserved concurrent drafts, renewed revision review and persisted
background/description rendering. Copy tests prove independent IDs, reset
checklist completion, excluded assignments/stars, fresh history and original
receipt recovery after a concurrent source edit. Lifecycle tests prove lost
archive/restore/delete receipt recovery, child-state preservation, read-only
reconciliation, consent/focus and deleted-parent denial.

The initial phone lifecycle run exposed delayed dialog-exit focus theft from
the retry button. The deterministic regression fails before repair; shared
owned-focus admission now preserves the user's chosen control. All 24 archive
component cases and 16 related focus/archive/observation cases pass, as do web/
browser type checks, lint and production build. Ten lifecycle and four copy
WCAG-tagged Axe scans pass in the executed native scenarios. See
[lifecycle repair](../board-archive-control.md#executed-lifecycle-recovery-and-focus-repair),
[metadata evidence](../board-metadata-control.md#executed-desktop-and-phone-metadata-recovery)
and [copy evidence](../board-copy.md#executed-desktop-and-phone-copy-recovery).

The local fixture mounts frozen API/Worker assemblies in cached runtime images
and permits unverified registered accounts, matching the ordinary CI browser
phase. It does not prove retained-current-image release acceptance, complete
permission/lifecycle matrices, stored-image native copies or performance.
Earlier estimates and pending local-native notes below retain their historical
revision context; the current estimate is 16% and the ticket remains open.

Both complete desktop/phone personal-star native scenarios now pass against the
local Production API, restricted schema-110 PostgreSQL and current Vite source,
with disposable Workers scoped only to their new fixture Organizations. They
verify independent private preferences, real private WebSocket delivery, original
receipt recovery after a later unstar, immutable history, automatic account
timezone recovery in an open mirror, unchanged Board/List data, keyboard closing
focus and reload. The full invocation exits 0 with two cases in 58.1 seconds;
this is not a performance benchmark. Workers retire afterward.

The first full run exposed an installed-Dialog closing-focus defect. Capturing the
owning Dialog before the Close button is removed now restores the history opener
without overriding external focus. The new regression fails before the repair;
all 63 combined star/activity/comment/identity component cases and web typechecking/
lint pass. See [star evidence](../board-star-preference.md). Local immutable builds
and Vite source do not establish current retained-image or complete Board acceptance.

## Star dialog background-read keyboard recovery

The [executed history preference and focus repair](prd-02-acceptance.md#executed-history-preference-recovery-and-star-focus-repair)
reproduces a removed star action losing keyboard ownership during a background
read. The regression fails before the repair; all 58 selected star component,
history, live and sync cases pass afterward. Actual desktop/phone star scenarios
then pass in one final invocation against the production bundle, restricted
PostgreSQL and separate scoped Worker, retaining original receipt/key, later-state,
private delivery, timezone recovery, unchanged Board/List, closing focus and reload
assertions. Browser fixture activation observes actual parent admission and focus;
the modal background flag is read directly because MUI correctly hides it from
accessibility navigation. Current retained-image/full Board acceptance is still
required. PRD-04 remains open at **16% estimated work remaining**, a planning estimate.

## Functional traceability

Both existing persisted-workflow cases in `tests/browser/board.spec.ts` now pass
locally at desktop and phone widths in the six-case PRD-05 invocation. Normal UI
creation persists the Organization, private Board, List and Card; lost successful
Card creation recovers exactly once with the same key. Card conflicts preserve
the draft until explicit discard, reload retains edits, wrong Organization scope
withholds Board content, and a separate anonymous browser views the public Board/
Card read-only. See the [sharing execution audit](prd-05-acceptance.md#current-local-execution).
This is scoped local Production evidence with the same frozen-assembly and
unverified-account policy limits; current-image, denied-creation and complete
Board acceptance remain required. The estimate remains 16%.

Four additional existing archived-Board account recovery native cases now pass
locally at desktop and phone widths against Production API, restricted
PostgreSQL and the production web bundle behind current Nginx/CSP. Restore and
permanent deletion preserve unchanged state before uncertain account admission
and recover the identical original command after a committed change whose
post-command account confirmation fails. Keyboard, focus, Axe, consent and
no-reload checks passed. See [executed archive recovery](../board-archive-discovery.md#executed-account-uncertainty-recovery).
This closes that local execution gap, while retained-image lifecycle, complete
Board acceptance and performance remain required. This increment retained the
then-current 17% estimate before the later metadata/copy/lifecycle execution.

All five existing Organization Board directory live scenarios now pass locally
in one 4.0-minute invocation. Two ordinary-directory and two archive-directory
cases cover desktop/phone genuine Worker delivery; the phone administrative case
also covers source filtering, demotion/regrant and Organization membership loss.
Canonical event IDs, reconnect recovery, private content withdrawal, retired
review/consent and unchanged state after refusal are verified. See
[live native evidence](../organization-board-realtime.md#executed-native-directory-delivery-and-withdrawal).
Historical pending-execution notes below retain their revision context; these
scoped local passes do not establish current retained-image or full acceptance.

| Requirement | Current implementation and relevant coverage | Evidence still required for closure |
| --- | --- | --- |
| BOARD-FR-001: authorized creation | OrganizationHome submits a private Board through the keyed work mutation transport; `tests/browser/board.spec.ts` exercises persisted creation and isolation at desktop and phone widths. | Execute the corrected built-in background submission against current release images, including denied creation and empty states. |
| BOARD-FR-002: canonical fields | Board snapshots and typed metadata/visibility producers retain Organization scope, name, description, background, lifecycle, clocks and revisions. | Complete current schema/migration, API and native acceptance; malformed responses must remain withheld. |
| BOARD-FR-003: rename and description | BoardMetadataControl owns reviewed revisions, retained drafts, conflict review and bound same-key recovery. `tests/browser/board-metadata.spec.ts` exercises two clients and lost committed replies. | Current-image native execution, permission withdrawal and unchanged protected state on rejected writes. |
| BOARD-FR-004: approved backgrounds | Fixed named colors plus Board-owned sanitized PNG references; selection uses checked Card attachments, separate Worker publication and explicit public exposure consent. See `board-background-images.md` and `BoardBackgroundImageContract.cs`. | Genuine image pipeline passed at `1655ef8`; staged Organization/membership and anonymous parent withdrawal passed in restricted PostgreSQL at `6fcc174`. Full current-image native/acceptance execution remains required. Synthetic native cases alone do not prove publication. |
| BOARD-FR-005: independent stars | Retained actor-scoped preferences, optimistic version checks, keyed receipts and separate current preference reads. `test-board-star-preferences.sh` and `tests/browser/board-star.spec.ts` cover persistence and clients. | Current release-image native recovery, independent actors and current grant withdrawal. |
| BOARD-FR-006: authorized copy | BoardCopyService atomically creates the independent graph, admission, fresh history and receipt. BoardCopyControl validates the original acknowledgment and current destination disclosure separately. Local desktop/phone native recovery now passes. | Older immutable-image foundation run `37854464485` passed both genuine image-backed native copy cases using the explicit private filesystem/ICAP test providers. Current retained-image execution, late rollback and admission tests remain required; this does not establish cloud-provider durability. |
| BOARD-FR-007: defined copy policy | See `board-copy.md`: non-deleted Lists/Cards, labels and checklists copy with new IDs; completion resets; membership, personal preferences, attachment metadata and source history are excluded. Board-owned image ownership copies independently. | Current restricted PostgreSQL full-graph fixture, source invariance, same-key recovery and native semantic assertions. Capacity correctness does not prove rendering latency. |
| BOARD-FR-008: archive and reopen | Reviewed Board archive and bounded authorized archive directory restore; `test-board-discovery.sh` and `tests/browser/board-lifecycle.spec.ts` cover both transitions. | Current-image desktop/phone keyboard execution, concurrent lifecycle changes and current admission withdrawal. |
| BOARD-FR-009: confirmed permanent deletion | Archived-only deletion requires elevated admission, reviewed version and explicit irreversible consent. `BoardDeletionConsentTests.cs` and native lifecycle coverage exercise refusal and recovery. | Current-image runtime proof, unchanged child state on refusal, inaccessible deleted-parent routes and retained original receipt semantics. |
| BOARD-FR-010: authorized activity | ActivityHistoryControl uses bounded current-scope history; ActivityFeedTests, ActivitySourceScopeTests and ActivityHistoryLifecycleTests cover scope and lifecycle. | Verify two-client current history and reconnect recovery against current images, including withdrawal of disclosure after admission loss. |

Paths ending in `.cs` above are under `tests/StrataAI.Api.Tests`, except `BoardBackgroundImageContract.cs` under `tests/StrataAI.Persistence.Contracts`. Shell fixtures are under `scripts/ci`. Feature controls are under `apps/web/src/features/kanban` except OrganizationHome under `features/organizations`.

## Acceptance criteria and full definition of done

- **AC-BOARD-04-01:** authorized creation must persist and render its authoritative result. Local component assertions or an HTTP response alone cannot prove the complete client action.
- **AC-BOARD-04-02:** unauthorized/invalid metadata writes must return the stable refusal, leave protected state unchanged and avoid protected disclosure. Run both current API authorization coverage and restricted persistence/release assertions.
- **AC-BOARD-04-03:** authorized clients must receive or recover current Board activity without a full manual reload. Inspect executed two-client/reconnect results, not just the presence of event producers.
- Every relevant mutation must retain atomic authorization, audit/event envelopes, durable outbox delivery and idempotency semantics; source tests and immutable-image runtime checks cover different boundaries.
- All thirteen linked test scenarios remain required. In particular, current desktop/phone keyboard, focus, WCAG, late responses, lifecycle withdrawal, concurrency and disconnect/reconnect results must pass. Collection/type checking does not execute them.
- BoardScreen now uses bounded List/Card viewport windowing with measured Card heights, retained work/drag/focus and canonical keyboard navigation; see `../board-windowing.md`. Both local desktop/phone native capacity cases now pass with actual restricted PostgreSQL, separate Worker, 200 Lists, 5,000 active Cards and 100,000 archived Cards; see [Kanban release evidence](../kanban-release-evidence.md#native-capacity-after-stable-window-content). Current retained-image capacity and full interaction/performance acceptance remain pending. These local cases and the preserved archived fingerprint do not establish general performance or release readiness.
- Retain the documented normal-condition budgets: Board usable rendering <1.5s, movement feedback <100ms, mutation p95 <500ms and cached detail <200ms. Existing normal desktop measurements do not establish the large-data case.
- Archive directory SignalR invalidation is now implemented, with acceptance still incomplete. Migrations 073/074 supply ordered canonical sources and actor-specific permission epochs; the audience reader, opaque cursor binding, transactional demo/PostgreSQL adapters, authenticated live endpoint and MUI archive consumer are implemented. At `691763c`, run 37264284540 passed all source-quality checks, including complete Domain/API host tests, web quality and restricted PostgreSQL integration. The desktop/phone native two-client, genuine upstream identity, disconnect recovery and membership-withdrawal fixtures compile and collect but await exact-image execution. Polling/foreground recovery remains an outage fallback. See `../organization-board-realtime.md` for the exact authorization and outstanding proof scope. This administrator archive feed does not prove realtime discovery for every active-Board reader or anonymous visitor.
- Archived directory reads now lock the qualifying Board membership row as well as the Board. The exact-image discovery fixture observes a restricted API lock wait, withdraws only the qualifying membership role, requires no archived name disclosure, and verifies a fresh grant restores discovery. The local warning-free build and shell checks pass; execution of this new race fixture remains pending CI.
- Complete schema/migration review, privacy-safe telemetry documentation, current source/integration/native checks and the no-known-P0/P1 audit before closure. The adopted deployment architecture and build-once release gates remain mandatory.

## Executed evidence and known limitations

At `4955e6f`, run 37262699727 passed source, image-build and security checks,
but container job 111615492874 failed step 59. The genuine desktop image/copy
case passed; the phone timed out before copy submission. Artifact 11325912285
retains the screenshot and native trace. The trace shows Copy Board enabled at
the assertion and disabled when the subsequent Enter keypress resolved it. The
failure screenshot retains Card details; no copy confirmation appeared. The
fixture now uses native click actionability for Close and Copy Board and proves
the Card dialog is gone and the copy dialog is visible before the existing
one-request/201 assertion. Timeouts, retries and assertions are not relaxed.
This repair awaits execution. Native capacity step 63 was skipped, so this run
neither proves nor disproves the BoardWindow measurement repair.

At `4aefb6f`, [run 37259415792](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37259415792)
container job 111605472900 step 59 passed both current genuine image-background
cases, including the one-request/201 copy confirmation repair. The same job's
step 63 passed the concurrent 5,000-Card rank-free move/replay check, but both
native capacity cases failed after their fourth keyboard ArrowDown at the real
dragged/target rectangle assertion. They progressed beyond the earlier seeded
List mounting failure; this does not satisfy complete windowing/keyboard proof.
Retained artifact 11325426430 (15,816,930 bytes) has SHA-256
`ef0578a780cd53dc42b1ee83b9874c0427dcde0082328a1f25bc9a39e2da98ec`.
The screenshots and native action traces were inspected. Newly measured
window rows can reposition later drop targets without resizing those targets,
leaving dnd-kit's cached rectangles stale. BoardWindow now requests a mounted
drop-target measurement refresh after committed layout changes during a drag;
idle layout changes do not refresh the drag cache. The focused window/keyboard/
Board drag suite passes 19 cases, with typecheck and lint passing. Native repair
verification remains pending; assertions, timeouts, budgets and retries stay
unchanged. The release is failed and the ticket remains open.

At `1655ef8`, [run 37253072119](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37253072119) container job 111586592581 step 59 passed the complete current genuine desktop/phone image-backed cases and HTTP archive/race/copy/private/public/revoked-session/clearing fixture; see `../board-background-images.md` for its exact scope and provider limitations. This resolves the earlier stale upload-Card revision failure. The container job subsequently failed step 63: both large-Board browser cases could not mount the seeded List after scrolling. The adopted CSS-variable MUI spacing expression had been parsed as a number, yielding invalid row positions. The current correction measures resolved browser spacing; all 13 focused BoardWindow regressions pass. Current exact-image capacity and complete browser acceptance remain pending. It is scoped background-image evidence, not a completed release or ticket closure.

At `e77195d`, run [37241937689](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37241937689) has successful .NET/API, PostgreSQL, web, image-build and security jobs. Its container job's step 57 passed the then-current actual upload/Worker publication, sanitization, private image selection, independent Board copy, source archive and anonymous denial assertions. The complete native suite was still running when this audit was written. This proves neither a green release nor later extensions to that fixture.

At `b56d14b`, run [37242836614](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37242836614) failed the genuine native image-selection cases with HTTP 415. Commit `2873f38` fixes the missing JSON Content-Type on selection. Commit `f77a215` repairs the same omission in Board copying; all 13 focused copy tests, web typecheck and lint passed locally. The genuine current-image runtime remains pending. These fixes do not justify checking off the whole native or copy acceptance scope.

Only close the issue after the current implementation passes the full relevant runtime coverage and the gaps above are resolved. Do not infer closure from a previously green revision, compiled tests, a collected scenario, simulated application replies or an individual successful CI step.

At 9c4eaeb, run 37263319182 container job 111617294144 passed both
genuine background cases and concurrent 5,000-Card rank-free movement/replay.
Native step 63 failed: desktop reached pointerAcrossBuffer but found no later
fully visible target; phone failed the keyboard rectangle alignment predicate.
Artifact 11326093609 retains both screenshots and traces, now inspected. The
window source itself can move when estimated heights above it are refined;
refreshing target measurements does not change the sensor's original source
rectangle. BoardWindow now retains the source row's starting layout position
for the active drag and restores its canonical position after drag completion.
A regression verifies source stability, moved target positions, canonical
restoration and bounded mounting. The window/Card/List keyboard suite passes
22 local tests with typecheck and lint. Exact-image verification remains pending;
the separate desktop pointer failure remains unresolved, and neither viewport
is claimed complete.

The desktop pointer trace places the pointer at the clipped Card viewport's
lower edge after a successful keyboard move. The installed dnd-kit auto-scroller
defaults to outer-first traversal, prioritizing the scrollable List section over
its nested Card viewport. BoardScreen now selects the supported inner-first
traversal order, retaining the existing admitted-container predicate and outer
scroll fallback for List/cross-List movement. The existing Board drag, Card
keyboard and BoardWindow suites pass 20 local cases; typecheck and lint pass.
This is a targeted nested-scroll repair awaiting native execution, not proof
that the desktop failure is resolved. Native predicates and budgets remain
unchanged.

Ordinary Board discovery now has a separate server-owned replay audience and
positive reader revision in the protected cursor. Archive cursors cannot be
reused for discovery, or vice versa; admission changes invalidate discovery
bindings. The PostgreSQL reader admits ordinary active Organization members to
Organization/public Boards and private Boards with an actual active Board grant.
Eligibility is checked before each bounded source query and private grants use
an inner join with tuple locks. Canonical source identity and readiness barriers
are retained. Restricted persistence assertions cover visibility withdrawal,
private MEMBER admission, grant withdrawal and original delivered event identity.
The full solution builds with warnings treated as errors. Runtime database and
cursor execution await CI; discovery transport, demo parity and active directory
UI are not yet implemented by this increment. PRD-04 remains open, estimated
20% remaining; no wider live acceptance is claimed.

Run 37267135324 passed PostgreSQL checks but its web suite failed one of 112
files: the Board copy recovery callback assertion ran before the passive effect.
Commit 929c1fd waits for the exact existing callback assertion; all 13 focused
copy tests pass. No browser retry, pacing, timeout or acceptance predicate changed.

The discovery audience is now registered separately in both runtime modes. Demo
replay uses actual Board visibility and active grant eligibility before its
window bound. Actor reader-grant and Organization visibility revisions have
rollback snapshots alongside existing archive revisions. An API-host contract
seeds 64 inaccessible canonical sources before an eligible MEMBER source,
checks the original event ID and audience isolation, then exercises admission
withdrawal and a refused transaction restoring visibility, grant and cursor
state. These are adapter assertions, not Worker/native acceptance. The full
solution builds with zero warnings/errors; execution awaits Linux CI because
local application control prevents .NET test execution. f65a9e8 run 37267850262
has passed web and PostgreSQL jobs, including the new restricted ordinary-reader
storage assertions; .NET and immutable-image stages remain pending at inspection.
Discovery transport and active directory UI still remain. PRD-04 estimate stays
20% remaining; issue remains open.

The existing authenticated Organization live hub now exposes server-selected
WatchBoards discovery beside Watch archive administration. Both methods share
the same connection subscription bound, cancellation release, origin routing,
pre/post-IO current-session checks and final delivered-cursor admission proof.
The client does not choose a reader audience through a cursor. A new API-host
WebSocket contract checks ordinary private MEMBER lifecycle delivery, rejects
reuse of a discovery cursor by the archive stream through a body-free reset,
then withdraws the private grant and confirms that a later canonical restore
is not disclosed. Full warnings-as-errors build passes; actual hub contract
execution and immutable-image coverage remain pending CI. The active directory
browser consumer still needs integration. PRD-04 remains open at 20% estimated
remaining work.

The browser live connector can now select the fixed WatchBoards discovery
method while archive callers retain Watch. A regression verifies initial reset,
canonical invalidation and exact opaque discovery cursor reconnect. All 15
connector tests, typecheck and lint pass locally. This does not yet integrate
OrganizationHome or prove a native ordinary-reader scenario. Current c820b65
CI run 37268321264 is live; demo increment run 37268199264 has passed PostgreSQL
and its API execution remains pending. No running CI has been cancelled.

OrganizationHome now subscribes to ordinary discovery after fresh account and
server-authorized directory admission. It verifies the current profile before
and after directory IO, withdraws cached Organization/Board names and creation
consent immediately on canonical invalidation/reset/interruption, aborts older
reads and reloads the authorized snapshot. Account changes redirect to sign-in
without exposing previous names. Content-free polite announcements describe the
access check. Nine component tests pass, including membership refusal after a
live reset, a delayed stale directory reply fenced by a newer invalidation, and
an account switch during IO; typecheck and lint pass. Native two-client/Worker
verification remains outstanding. PRD-04 stays open at 20% estimated remaining.

Run 37268321264 PostgreSQL failed at the existing Board background image atomic
retry assertion, after earlier preview/cover contracts passed. Commit b481732
retains that exact predicate and adds only operation status/code and numeric
revision diagnostics to distinguish the cause. No object location, content,
identity or credentials are emitted. This is diagnostic progress, not a repair
or green CI claim; full .NET warnings-as-errors build passes.

Two new native discovery scenarios (1280px/390px) are collected in the existing
release-proxy Organization live suite. Each registers a real ordinary account,
accepts a MEMBER invitation, admits one private Board through a MEMBER grant,
opens two clients and observes genuine upstream frames. An inaccessible actual
Board source precedes the admitted archive; both clients must retain the
original /sync event ID, withdraw the active Board, recover its real restore,
and then clear private names and creation consent after grant removal without
logging out. Envelope allowlists and viewport overflow assertions are retained.
Browser fixtures typecheck and all five Organization live cases collect. These
new native cases have not executed yet; immutable-image acceptance is pending.
PRD-04 remains open, estimated 20% remaining.

Run 37268321264's web failure was a test reading watchBoard's callback before
its passive subscription effect ran. 3c596b8 waits for that exact registration;
all 26 ArchivedListsPage component cases pass. No assertion, pacing, browser
retry or timeout was relaxed. Later b481732 PostgreSQL and full web jobs passed;
the earlier background retry failure remains under diagnostic observation.

The ordinary directory native scenarios now also re-grant the actual private
MEMBER permission, require a fresh visible Board without resurrecting the old
creation dialog, and remove actual Organization membership by its current
version. Both clients must then withdraw Organization/Board names, while a fresh
Board-directory request returns 404 and the current login remains valid. Browser
fixture typecheck and five-case collection pass. Actual release execution remains
pending; this is additional acceptance coverage, not proof of completion.
PRD-04 stays open at 20% estimated remaining.

## Background recovery focus and failed-case fixture isolation

[Run 37598829037](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37598829037)
at `52eb4b64` completed container job `112725599671` with failure in the
explicit attachment/image pipeline. Desktop recovered the background save but
failed return focus to its Review button. Phone then found the shared source
Board PUBLIC because desktop had failed before its ordinary cleanup. Required
CI failed and the release bundle was skipped. Browser artifact `11473659605`
was downloaded and its SHA-256 verified as
`50214ec9844e8317eec05b42c5b0c06f1f6cbc7ccf109f2c56ba6c7ce2a66168`.

The control now retains owned return-focus recovery after acknowledgment, so a
subsequent access refresh can disable/re-enable the Review button without losing
its original return target. An intentional focus move to another control still
cancels recovery. The regression reproduces the failure before the repair and
passes after it, including no focus theft and original request/key replay.
All 18 background-control/image tests pass, with web/browser TypeScript and lint.

Each native image case now has an unconditional afterEach fixture cleanup. It
signs in normally, reads/validates the actual scoped Board and latest revision,
clears the source image when necessary, restores PRIVATE visibility and verifies
the canonical baseline through ordinary version-checked commands. Existing public
consent/refusal, private copied ownership, image bytes, keyboard focus, session
withdrawal and WCAG assertions remain unchanged. This prevents a failed desktop
case from fabricating a mobile precondition failure; it does not make the failed
case pass. The corrected complete exact-image pipeline remains pending. PRD-04
stays open at 18% estimated remaining.

## Current Board issuer authority delivery

Actual Board membership additions and role changes now enter PostgreSQL's
existing private invitation authority pipeline through forward migration 113.
Both unchanged desktop/phone native scenarios pass rename, archive/restore and
issuer downgrade: stale acceptance/private Board names are withdrawn, focus and
content-free frames remain, and independent acceptance/current Board reads are
refused. Three source-family routing/retry cases and existing bounded private
capability checks pass with migration/readiness checks. See
[complete evidence](browser-recovery-ci.md#current-board-membership-authority-and-actual-recipient-interruption).
Estimated PRD-04 work remaining stays **16%** (planning estimate). Current immutable
CI, remaining release failures and full-PRD acceptance still prevent closure.

## Archive directory source and keyboard admission

Desktop/phone native lifecycle cases now pass original lost archive, restore and
permanent-delete receipts together. Consent and retry wait for the actual scoped
Board event and a protected directory read started afterward, then one enabled,
focused keypress. Child lifecycle, unchanged original keys/bodies/versions,
deleted-parent rejection, privacy withdrawal, focus and WCAG checks remain. The
desktop baseline failed before DELETE dispatch; the retained older retry also
sent no second DELETE. See [execution evidence](browser-recovery-ci.md#archive-source-admission-and-navigation-completion-boundaries).

Estimated PRD-04 work remaining stays **16%** (planning estimate). The separate
retained phone capacity failure, current immutable CI and full-PRD acceptance
remain outstanding; this focused success does not justify closure.

## Complete-history large Board capacity admission

The final current compiled Production API/real Worker/MUI/PostgreSQL schema-113
capacity invocations pass both desktop/phone cases at normal CPU (2.0 minutes)
and CPU-four (3.7 minutes). The actual 200-List/5,000-active-Card fixture retains
keyboard and pointer movement across mounted buffers, cancellation, empty-List
transfer, stable focus and exact persisted placement. Concurrent/replay checks
and post-browser fingerprints of all 100,000 archived records pass in both runs.

Readiness follows actual server cursors through the final non-pending page;
the same scoped Worker runs from before Board publication through acceptance.
Keyboard-drop admission and prompt edge stopping preserve every original target,
version and mutation assertion. Four new mandatory readiness regressions and all
22 related observer cases pass. See [complete evidence](browser-recovery-ci.md#board-delivery-readiness-follows-the-complete-bounded-history).
Estimated PRD-04 work remaining stays **16%** (planning estimate). Current immutable
CI and full-PRD acceptance remain required before closure.

## Strict verified-account Board management phase

The untouched metadata scenario fails strict-policy admission at immediate
login (expected 200 / actual 403). Eight complete browser files now use the
existing strict account fixture for fresh registration, pending-account refusal,
disposable-account verification and actual login: persisted Board workflow,
metadata, copy, lifecycle, archived-account recovery, personal stars, activity
history and stored-background client interactions. The optional-verification
path preserves its registration/login behavior in the unfiltered full suite.
No product authentication policy is relaxed.

The new mandatory phase selects all eighteen existing cases without a grep,
requires API/Worker verified-email policy and normal rate pacing, and runs after
strict Organization departure before watch/notification producers. All 111
previous named steps remain, plus this phase (112 named steps), within the same
seven isolated matrix executions. The workflow verifier requires every selected
file, the strict fixture flag, both host-policy checks and phase ordering. Three
omission mutations fail with the previous verifier (62 pass / three fail); all
65 guards pass after strengthening. Browser TypeScript, workflow syntax and
complete 320-case/124-file four-shard coverage pass.

The complete eighteen-case local strict invocation finished with seventeen
passes and one desktop lifecycle failure: original archive recovery succeeded,
but keyboard activation of Manage archived Boards did not navigate. A component
regression reproduced delayed archive focus restoration overriding an already
chosen destination (eight existing passes / one new failure). The product now
checks ownership of the closing dialog before restoring focus, retaining the
closing paper through its exit transition. All 36 related archive/directory/focus
tests pass, as do web TypeScript, targeted lint and a fresh production web build.
Both unchanged desktop/phone lifecycle cases pass in one 1.7-minute invocation
with zero skips/retries against isolated compiled API/schema-114 fixtures and
the new web output. Owned containers/database were removed. A fresh complete
eighteen-case phase is running; its outcome remains unverified.
This scoped local runtime is not the current immutable-image CI gate. Original source/receipt/concurrency/privacy,
keyboard/focus, accessibility, viewport and recovery assertions and deadlines
remain. Stored-background client cases still simulate publication, receipt and
PNG delivery; they do not replace genuine object-storage/API/Worker publication
acceptance. Current immutable/full CI, complete role/lifecycle and stored-image
copy/performance requirements remain. Estimated PRD-04 work remaining stays
**16%**; the issue is open.

## Strict genuine Board directory realtime coverage

The untouched ordinary-directory desktop case fails under verified-email policy
at immediate login (expected 200 / actual 403). All five existing genuine
`organization-board-live.spec.ts` cases now use the shared strict registration
fixture: fresh registration, pending-account refusal, disposable account
verification and real login. The optional fixture path preserves ordinary full
suite registration/login. Server authentication policy is unchanged.

The mandatory strict Board phase now selects nine complete files / 23 cases,
adding both ordinary-reader cases, both administrator archive cases and the
phone audience/role/membership-withdrawal scenario to the previous eighteen.
These cases observe real upstream WebSocket frames and canonical Worker sources,
original event IDs, disconnect/resume and current private directory withdrawal;
they do not fabricate event delivery. All original deadlines, retry policy,
source checks and protected-state assertions remain. A new omission mutation
fails the previous guard (65 pass / one fail); the strengthened guard passes
all 66 checks. The 112 named checks and seven matrix executions are retained.

The fresh complete five-case local strict directory invocation passes in
3.8 minutes, with zero skips/retries/flaky cases. It uses the compiled strict
Production API/separate Worker, freshly built MUI assets and restricted
schema-114 PostgreSQL 17/pgvector. Owned containers/database were removed. The separately running complete eighteen-case post-focus-repair
invocation likewise remains pending. Current immutable/full CI and all remaining
acceptance are required. Estimated PRD-04 work remaining stays **16%**.

## Older immutable-image command evidence

At `1f672945`, run `37854464485` command job `113595872446` completes
successfully. Its logs confirm restricted-role private/archived/deleted Board
discovery and current membership revocation; full-capacity independent Board
copy with distinct labels, fresh contents, complete-graph publication rollback
and atomic original-key recovery; and archive directory 50/2 paging with minimal
fields, deleted exclusion, current administrator admission/demotion and
nonmember denial. The same job passes supported-scale Board/List/Card
archive/restore and permanent-deletion commands with retained child/history
state, exact retries and per-action p95 below 500ms. These are executed older
immutable-image command boundaries, not current browser rendering budgets or
a green full release. The older notification job fails the already-corrected
post-read clock expectation; remaining full browser shards and current CI are
not certified successful. All current acceptance requirements remain in scope.

The same mandatory Board phase now also includes the five PRD-05 native
visibility/member/live-administrator cases (twelve full files / 28 cases), with
all previous 23 intact. Strict admission and genuine permission/transport
assertions remain; all 69 verifier guards pass. See the
[permission acceptance scope](prd-05-acceptance.md#strict-native-visibility-and-member-administration).
The complete permission invocation and separate eighteen-case management
post-focus-repair invocation remain pending; no combined 28-case pass is claimed.

## Complete post-archive-focus native management result

The complete eighteen-case strict management rerun passes in 14.2 minutes, with
zero skips/retries/flaky cases, using the frozen web output containing archive
focus ownership repair, current compiled strict Production API/separate Worker
and restricted schema-114 PostgreSQL 17/pgvector. All original source/receipt,
concurrency, current disclosure, keyboard/focus/mobile and accessibility checks
remain. The earlier 17/18 failure stays recorded above. Owned containers and
database were removed. Two background client cases still simulate publication
and PNG delivery; genuine provider gates retain their separate evidence scope.

The additional five genuine directory cases pass in 3.8 minutes, and the five
native visibility/member/live-administrator cases pass in 3.5 minutes, separately
with zero skips/retries/flaky cases and cleanup complete. These are three scoped
invocations, not a single combined 28-case pass or immutable-image CI success.

## Copy dialog closing-focus ownership

A regression using the real MUI copy dialog reproduces its exit callback moving
focus from an already chosen external link to Copy Board (13 existing passes /
one new failure). The copy dialog now checks ownership before restoring focus,
retaining its closing paper across unmount. Its own closing descendants still
return to the opener normally; an external destination retains focus. A first
repair withheld normal copied-link return focus, and the existing test caught
that error. The corrected repair passes all 26 copy/archive/focus tests,
including normal return focus, with TypeScript, targeted lint and a fresh web
build. Original acknowledgment, account/role admission and recovery are unchanged.

Unchanged desktop/phone copy verification and a fresh complete 28-case strict
phase are running against the new web output; their results are not certified.
The completed eighteen-case run above predates this copy-focus change. Current
immutable/full CI and remaining full-PRD acceptance still govern closure.
Estimated PRD-04 work remaining stays **16%**.

Both unchanged desktop/phone copy cases now pass after closing-focus ownership
repair in one complete 1.6-minute strict invocation, with zero skips/retries/flaky
cases. Original private graph, lost original response, concurrent source,
account-bound destination disclosure, one-request and keyboard navigation checks
remain. This uses the newly frozen rebuilt web output, compiled strict Production
API/separate Worker and restricted schema-114 database; owned containers/database
were removed. The chosen external destination regression is component evidence;
these native cases retain their existing normal copy recovery/navigation scope.
The complete combined 28-case invocation remains active and uncertified.


## Original restore recovery during live directory invalidation

The completed member-focus 28-case phase fails both lifecycle cases at restore
request count (expected two / actual one), after phone history also fails. A
previous description called these focus failures based on the source location;
the structured errors instead establish the count mismatch. Exact native cause
is not established by that mismatch alone.

A real-MUI regression independently reproduces original retry retirement when
live directory invalidation arrives during its pre-submit account check:
24 existing cases pass / one new regression fails before repair. New consent
still retires on a changed review epoch. An already submitted original instead
retains its immutable actor/key/body across that directory invalidation, with
current server authorization and both account checks still required. Account
withdrawal/unmount cancels the pending command through the existing fences.

The native workflow now explicitly requires `Board restore acknowledged.`
before reading the empty directory/focus and asserting two identical requests;
missing rows cannot stand in for this command's receipt. Original deadlines,
retry identity, command count, role/lifecycle and accessibility checks remain.
All 37 selected archive/focus components, web/browser types, targeted lint and
fresh web build pass. Both complete strict native lifecycle scenarios are
running against rebuilt assets/current compiled API/separate Worker/schema-114
PostgreSQL. Current immutable/full CI and full acceptance remain required.
Estimated PRD-04 work remaining stays **16%**.


## Complete original-key lifecycle recovery verification

Both complete strict desktop/phone lifecycle scenarios pass in 1.9 minutes,
with zero skips/retries/flaky cases. They now explicitly require the original
restore acknowledgment before checking the empty directory/focus and exactly
two identical key/body requests. Genuine two-client Worker updates, current
actor/role admission, child lifecycle preservation, keyboard/Axe and original
deadlines remain. Fresh original-recovery web, rebuilt Production API/separate
Worker and restricted schema-114 PostgreSQL17/pgvector were used. Owned
containers/database were removed. This scoped pass does not certify the
combined 30-case or current immutable/full CI; remaining full acceptance applies.
Estimated PRD-04 work remaining stays **16%**, PRD-18 **16%**.


## Complete combined strict Board verification

The [complete fourteen-file/31-case strict invocation](integration-ci-groups.md#complete-fourteen-file-strict-board-invocation) passes together in 21.7 minutes with zero skips/retries/flaky cases. Current compiled Production API/separate Worker, frozen original-key recovery web, restricted schema-114 PostgreSQL17/pgvector and verified-email enforcement were used; owned fixtures were removed. Original complete scenario assertions and deadlines remain. This is local runtime evidence, not current immutable/full acceptance. Estimated PRD-04 work remaining: **15%**.


## Executed source-deletion image ownership

The [Linux source tombstone and Board-image proof](attachment-object-storage.md#source-tombstones-and-independent-board-image-ownership)
permanently deletes an archived attachment, refuses all three original delivery
routes without provider reads and refuses current-version restoration, while both
independent Board background identities continue delivering their shared sanitized
PNG. The complete original image scenario still verifies archive/corruption/public
withdrawal/retirement afterward. It passes with zero skips/failures in 7.7 seconds;
locked build has no warnings/errors and the owned Linux container is removed.
Synthetic publication/storage, actual source-host sessions/commands/staging/HTTP,
and immutable/full/provider/backup-erasure limits remain explicit.
