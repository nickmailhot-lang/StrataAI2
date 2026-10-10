# Browser recovery CI investigation

Run 37061592501's exact-image browser job completed with five failures. The real
notification-center desktop/phone recovery scenario passed. Failed cases were
archived List return focus at 390px, Card deletion review at both widths, label
creation recovery at 1280px, and phone label-filter opening.

Archived Lists did not preserve the return target when a subsequent Worker-driven
read disabled the refresh button. Its focus restoration now waits for the MUI
dialog to exit and remembers focused refresh controls through background reads.
A regression simulates acknowledged restore, focus return, a pending live read
that loses focus, and restoration after that read; all 22 List archive tests pass.

Card deletion previously focused a possibly disabled primary-page button after
waiting only for the other page's heading. The browser case now waits for its own
live state, enabled review control and verified keyboard focus before Enter.
Label creation's failed DOM still had the color listbox open. That case now waits
for the selected menu to exit, checks the enabled submit control and verifies focus
before Enter. The filter case also explicitly verifies focus before its keyboard
activation. These are condition-based waits, with no skipped scenarios, relaxed
success/retry assertions, repeated mutations or fixed delay substitutes.

A local real-Chromium diagnostic with mocked transport confirmed that label
selection/menu-exit timing overlaps the next keyboard operation; it is diagnostic
evidence only, not a replacement for exact-image integration. The new browser
assertions and List focus repair require a fresh exact-image CI run. The prior
failed required-CI gate remains failed, and no ticket is closed by this change.

Run 37074806985 passed .NET and PostgreSQL source checks but failed one archive
component test: its live invalidation callback was invoked before the subscription
passive effect attached. The test now waits for that callback readiness, keeping
the three invalidations, exact read counts and non-aborted pending read assertions.
This is a test setup correction; it does not weaken the slow-read behavior check.

## Release browser run ea6104d and follow-up repairs

Run 37071997382 proved its exact-image Watch fixture but failed four of 72 browser
cases: Card label retry keyboard activation/focus, expanded phone assignees after
live refresh, opening a List deletion review, and closing Watch dialogs. Its
trace showed a phone focus-triggered Watch read between focusing Done and Enter,
and showed no second label PUT after the retry focus/keyboard pair. Failed DOM
snapshots and component keys corroborated disclosure loss during Board refresh.

Watch Done can now cancel a read-only check, abort its pending scope and dismiss
without waiting for a poll. Outstanding mutation/recovery intent still prevents
closing. The common work transport now checks cancellation before fetch and after
transport/body completion so a mock/provider that ignores cancellation cannot
start a later request or command after its scope is retired.

Label/assignee disclosure choice now survives revision/access refresh. Data stays
in a separate revision/access-scoped component: stale requests are aborted, old
names/pages disappear and a fresh scoped read starts automatically when admitted.
Board refresh no longer remounts disclosure state merely because reload/version
changed. The two-client phone helper accepts an already expanded region and keeps
its exact member-content and recovery assertions.

A passive List deletion/restore review can open from the retained archive page
while a current read is pending. Confirmation remains disabled until admission,
current impact, revision, explicit destructive confirmation and write state are
verified. No command is queued by opening the review. Label retry uses a verified,
enabled keyboard target and presses Enter on that target in one browser action;
its original key/version, two-write equality and returned-focus checks remain.

The 80 targeted transport, Watch, labels, assignee and archive tests passed, as did
typecheck and lint. Seven new cases prove read/command admission, cancellation/reopen, stale-name isolation,
retained disclosure, review/read separation and aborted command chains. Full web
suite/build and exact release browser acceptance still need current-run evidence.
No ticket is closed by these repairs.

Follow-up Watch admission separates a read-only Board refresh from a command
blocker. A valid currently admitted entity can open a fresh server-authorized
personal Watch read during background refresh; Watch/Unwatch/recovery commands
stay disabled until the Board refresh completes. This avoids losing the opening
key press to a transient read while preserving mutation admission.

The default full local web run passed 648 of 650 cases; BoardFilterControl's
25-member cap scenario and ListCopyControl's reviewed-authority scenario reached
the existing five-second test timeout. Both files passed all 40 cases when rerun
with one worker, without source/test/timeout changes, and the production build
passed. The follow-up admission change and BoardScreen passed all 48 focused cases. Current
Linux source CI and exact-image browser results remain the authoritative release
checks; no timeout, performance budget or acceptance assertion is waived.

Run 37073126224 also finished with failures: two Card label retry focus cases,
expanded phone assignees, List copy review, Card Watch opening and 102ms movement
feedback against the unchanged 100ms budget. This evidence adds the List copy and
feedback path to the remaining CI investigation; it does not invalidate its
already passed exact-image Card date and Watch fixtures or prove browser closure.

The follow-up drop path publishes presentation-only placement in the pointer or
keyboard drop event, before the command control mounts. It uses the current
canonical Card revision; stale, disabled or invalid destinations retire a preview
without writing. Persistence, original idempotency intent, uncertainty rollback
and refresh remain owned by CardMoveControls. A BoardScreen regression observes
the destination DOM before the POST starts, then proves rollback and unchanged
canonical data after response loss. The release feedback target remains <100ms;
only the exact-image performance case can establish that budget.

List copy discovery can open during a read-only Board refresh and still fetches
server-authorized destinations. Preparing or confirming a copy waits for the
current Board read. A refreshed source revision invalidates the original review;
an unresolved copy continues to retain its original version, body and key.
The 61 existing/focused movement, copy and Board tests plus the new drop regression
passed locally, as did typecheck, lint and production build. Exact-image acceptance
is still pending; these changes do not close a ticket.

CI 37147498536 (c59cdb8) completed with 96 passing, nine failing and one
skipped browser scenarios. Failures cover desktop filter keyword retention,
Reminder retry/focus, phone checklist collaboration, checklist feedback
observation, native download focus, live label focus and URL creation readiness.
The raw Reminder replay acknowledgment is valid UTC with the original successful
version/generation. Its apparent nonzero timezone offset in an initial diagnostic
read came from PowerShell's JSON date conversion, not the API. Network evidence
contains the original routed POST's successful upstream response but no retry
POST from the browser: readiness must be checked again after peer delivery.

Filter handlers now capture primitive field/checkbox values before functional
state updates. File review and Reminder/label writes park focus in the same MUI
Dialog before disabling the actually focused activating button; asynchronous
restoration still refuses another control or dialog chosen by the user. The
actual Dialog regression no longer manually supplies that fallback. Keyboard
fixtures wait for enabled admission immediately before Reminder retry, URL
creation and checklist management/item selection.

Checklist feedback observation starts at the user's enabled keyboard/click
activation on the stable document, observes the actual busy status and disabled
submit control, and waits for a paint. Unrelated form submissions cannot consume
the observer. Missing feedback still fails; the 100ms feedback, 200ms detail,
1500ms usable Board and 500ms mutation p95 budgets remain unchanged. Browser
discovery is not execution evidence and none of these repairs closes a ticket.

Browser acceptance source validation now has an explicit strict TypeScript project covering playwright.config.ts and every tests/browser TypeScript fixture/helper. The root pins Node 24 declarations to 24.19.1 in the lockfile and exposes npm run typecheck:browser; web-quality executes it after the SPA typecheck, before browser runtime stages. Three performance fixtures now explicitly reject a missing browser observer before awaiting feedback, with their existing budgets and sampling unchanged. Full browser and SPA typechecks pass locally, along with both readiness tracker regressions and all seven retained performance-evidence cases. This resolves the missing-declaration limitation recorded for the notification fixtures; static validation is still distinct from actual exact-image runtime acceptance, which remains pending.

The stable full web run for departure revision `ac4502d` completed with
1,508 passing tests and one 5-second timeout in the existing Board filter
50-person paging/25-assignee-cap case (123 files, 202.43 seconds). That case
passed in isolation with the original source. Its repeated checkbox role
searches now use accessible label queries and retain the first connected
checkbox reference; the checkbox type, disabled cap, paging replacement,
selected count and exact cursor request assertions remain. All 35 filter
cases pass locally. The original 5-second timeout is unchanged. This focused
result does not establish full-suite or native release acceptance.

The subsequent stable full web run at `550f7d9` completed with 1,508 passes
and one 5-second timeout in the existing URL-attachment original-retry Board
case (123 files, 201.73 seconds). The assignee-cap case passed. The URL case
passed in isolation before and after scoping its link controls and success
query to the accessible Create link attachment region. Original body/key,
newer snapshot, competing-mutation fences and post-recovery enabled controls
remain asserted, with the original 5-second timeout. Full/native acceptance
is still pending; these source observations do not establish release readiness.

The next stable full web run at `52e8287` passed: 1,509 tests in 123 files,
224.25 seconds, exit 0. It includes the assignee paging and URL attachment
query changes and all current departure source cases. Browser files remained
unchanged during the run. Backend-only removal receipt work does not extend
this source result to API, PostgreSQL or exact-image browser acceptance.

Container job 112108234170, run 37410994157 at `400db3c`, finished with
128 browser passes, 31 failures and one skipped case. Its retained compose
diagnostics show repeated PostgreSQL 40P01 cycles: a Work request holds a
Board lock and waits for users FOR SHARE, while navigation holds users
FOR UPDATE and waits inside append_or_replay_navigation_interaction for the
Board. These storage refusals affect checklist/filter/live recovery and direct
Board reads; increasing browser timeouts would not repair the transaction cycle.

Interaction producers now use a distinct observation boundary. A scoped
Organization parent is admitted before the account, then an actor-private
transaction advisory lock serializes history/receipt work, and account admission
uses FOR SHARE. Profile commands retain FOR UPDATE. Migration 085 applies the
same parent/advisory/shared-account order to the restricted navigation, search
and filter functions, including direct database calls. Current account/session
checks, target visibility, original receipt identity, expiry, private metadata
and final-session rollback remain required. The actor gate also serializes
search-stream and filter/navigation receipt writes across interaction types.

The mandatory native navigation fixture holds the actual Organization/Board
locks under the API runtime role, observes a real navigation function waiting,
then reads the same actor FOR SHARE before releasing the Board. Both database
completion and a canonical navigation 200 are required; the old cycle would
fail this check. API/persistence compilation and Bash syntax are source evidence;
native execution and complete browser acceptance remain pending CI.

The full web run at `64ad06f` completed with 1,514 passes and one existing
List copy discovery/current-revision case exceeding its 5-second timeout
(123 files, 194.03 seconds). All member-removal cases passed. The List copy
case passed in isolation with the original source; its reload/review/confirm
controls are now queried once and checked for attachment after rerenders.
The original refresh/version invalidation, disabled/enabled controls, exact
read count and 5-second timeout remain asserted. All 17 List copy tests, web
type checking and lint pass after this change. Full revalidation is pending.

The stable full web recheck at `526812b` passed all 1,515 tests across 123
files (200.74 seconds). This proves the web source suite at that revision;
it does not substitute for pending exact-image PostgreSQL/browser acceptance.

Organization creation now retains an immutable account/body/key intent after
unknown outcomes, checks canonical current membership before opening the
Organization and fences account switches, deadlines and late route responses.
All 45 focused creation/discovery tests, web TypeScript and lint pass. Browser
TypeScript covers the desktop/phone exact-image scenario, which requires a real
committed creation and later metadata to survive a lost-response same-key retry.
Native execution and the expanded full web run remain pending.

CI run [37432005432](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37432005432)
at `f776b1d` completed with PostgreSQL integration and web quality passing, but
Domain tests failed (628 passed, 15 failed). All 15 reported the same unresolved
`ILogger<InMemoryIdentityUnitOfWork>` during standalone Identity module
composition. The identity registration now adds logging services, preserving
host-configured providers and transaction diagnostics. Source compilation does
not establish runtime repair; the Domain/API stages must run again in CI.
Images/container/security/release stages were skipped after the failed source
gate, so this run provides no exact-image browser or release acceptance.

The stable expanded web run at `5cfe5af` completed with 1,528 passes and two
5-second timeouts in existing Board metadata-recovery and append-move cases
(124 files, 208.84 seconds). Both cases passed in isolation with original
source. Their field/action queries now use the accessible dialog scope, with
close/current-move controls retained and checked for document attachment.
Original immutable request/key, competing-command fences, provisional placement,
canonical reload and focus assertions remain; the 5-second timeout is unchanged.
Both focused cases and web TypeScript pass after the query changes. Full
revalidation remains pending.

The stable full web recheck at `97f723a` passed all 1,530 tests across 124 files
(220.77 seconds), including creation recovery and both scoped Board cases.
This establishes web source evidence at that revision; native exact-image
Organization acceptance remains pending.

CI run [37433183371](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37433183371)
at `eda44a7` confirms the logging repair: all 643 Domain tests pass. Web and
PostgreSQL quality stages also pass. API-host tests finish with 498 passes and
one invitation microsecond-precision fixture failure. The fixture dereferenced
`ImplementationType` although Demo invitation storage now registers an interface
factory over a singleton rollback participant. Its wrapper now invokes the
original factory, preserving that underlying participant and precision assertions.
Compilation cannot establish fixture repair; API-host CI re-execution is pending.
Image/container/security/release stages were skipped by the failed source gate.

Organization deletion now has explicit Owner confirmation and immutable
account/version/key recovery. Its independent operation route checks canonical
Owner admission directly, so normal surface polling cannot destroy recovery
after DELETING withdraws access. It distinguishes a 202 request acknowledgment
from completed deletion, fences deadlines/late responses and keeps keyboard
cancel/recovery/status focus. Focused source and routing checks pass; native
Owner/Admin, lost-response, accessibility and desktop/phone scenarios await CI.
Expanded full web verification remains pending.

The combined deletion, Organization discovery and application routing check
passed all 53 tests across three files. An earlier combined run reported one
existing Board paging focus assertion failure; that case passed in isolation
and in this combined recheck without changing the assertion or timeout.
CI run [37434723078](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37434723078)
at `96394b9` passed web, PostgreSQL and .NET quality and the source gate,
confirming the invitation fixture repair. Image verification is still pending;
these source results do not establish native browser or release acceptance.

The same run subsequently built the immutable images and passed security,
but container integration finished with 153 browser passes, 14 failures and
one skip. Failures cover Board copy/lifecycle/star, Card labels, checklist
reads, deadline/label live filters and navigation observations. Both native
Organization creation recovery viewport cases passed. Required CI failed and
the release bundle was skipped; Owner deletion browser scenarios are new
and were not included in this revision. Current web type checking, lint and
browser test TypeScript checks pass for the Owner deletion implementation.

CI run [37488625810](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37488625810)
at `7b376c9` completed web execution with 1,547 passes and three failures
(125 files). All failures are in `boardFilterChange.test.ts`: fixtures create
originals at a fixed October 5 clock but three retention calls default to the
actual clock. Once 24 hours elapse, valid expiry enforcement refuses those
fixtures before their intended assertions. Repair must pass the fixture clock
explicitly; production expiry and expired-dispatch/storage assertions remain.
PostgreSQL source integration passed; .NET execution is still pending.

The full local run at `7b376c9` completed with 1,530 passes and 20 failures
(125 files, 438.04 seconds): the three clock fixtures plus failures in existing
UI cases, predominantly 5-second timeouts. This is failed full-suite evidence,
not a passing revalidation. The fixture repair supplies the same explicit clock
to creation and retention. Navigation native scenarios now configure and restore
the real Worker scope before requiring delivery; Board copy checks its disabled
background control with an explicit hidden-element query while MUI owns modal
accessibility. Original retry, event delivery and disabled-state assertions remain.

Archived Board acknowledgment recovery now explicitly requests focus return
when live invalidation has already closed its review, and preserves a focused
refresh target through a background read. The existing lost-response/live-reset
regression now focuses Retry and asserts the refresh target after acknowledgment.
All 15 archive-directory component tests and all 10 filter-intent tests pass.
Browser TypeScript passes for the native fixture repairs. Native execution and
expanded full verification of these changes remain pending.

All 239 tests across the 12 previously failing local UI files passed with
`--maxWorkers=2` (168.90 seconds), without changing their assertions or
5-second timeouts. This supports a local concurrency/timing explanation for
those failures, but is not full-suite evidence. A complete recheck with the same
worker bound remains required. CI continues to run the normal source gate.

The complete local web recheck with `--maxWorkers=2` passed all 1,550 tests
across 125 files (769.48 seconds). Browser source remained frozen at `350f042`
throughout execution; subsequent commits changed documentation and .NET only.
The default-concurrency CI web gate at `350f042` also passed, followed by
PostgreSQL, .NET and the source gate. Immutable image build/verification remain
separate gates; these source results do not close native or release acceptance.

## Later terminal failures and repairs

The `412f31e` exact-image container job failed in comment-mention commands after
earlier credential-retry checks passed. Its disposable eligibility fixture
restored Organization membership at the same revision, colliding with the private
immutable activation proof introduced by migration 097. Withdrawal and restoration
now advance revisions and update timestamps for each actual fixture transition.
The proof guard and canonical publication requirements remain intact.

The old `5a2433f` browser job finished with ten failures: Board restore, two Card
label-filter cases, two deadline-filter cases, three navigation cases and two
Organization deletion cases. Filter opening now locates the labeled control
independently of the dialog's temporary accessibility hiding while the MUI menu
is open. A confirmed navigation visit no longer resubmits during live read
re-admission; new visits still create fresh originals. Deletion's Card denial
check uses the existing protected labels endpoint instead of the unsupported
bare Card GET, which returned 405 rather than testing confidentiality.

These are repairs for observed failures, not passing native evidence. Local
navigation component tests, TypeScript/lint and shell syntax pass; all corrected
restricted and native scenarios require the next exact-image execution. The
Board restore consent race is addressed below; the full release gate remains
incomplete. No failed case is skipped or removed.

### Board restore trace and withdrawn consent

The retained `5a2433f` browser trace confirms that the initial archive read
completed before SignalR's initial reset. The restore review opened during that
gap: the enabled assertion passed, then the reset disabled the button before
Playwright pressed Enter. No restore POST was sent. The resulting page correctly
withdrew consent and completed another protected archive read.

The archive confirmation now stays disabled when no reviewed Board or unresolved
original exists, including the MUI exit interval after re-admission succeeds.
The component regression invokes the real live callback, confirms no stale write,
then opens a fresh review and sends exactly one command. The native scenario waits
for the initial live reset's protected archive-read announcement before opening
restore consent. It retains the keyboard command, lost actual acknowledgment,
original-key retry, observer delivery and child-state assertions. Current native
execution remains pending exact-image CI.

The older `180b46c` run `37545735746` subsequently reached a terminal container
failure in job `112552757473`: the comment-mention fixture attempted to recreate
an Organization membership activation without advancing its entity version,
violating `organization_membership_activations_pkey`. Its actual release identity
and invitation command checks had passed before that step. This image predates
the `f3daad3` fixture repair; it is not evidence against that later repair. The
`f3daad3` run `37547634517` has passed source gates and build-once image creation;
its security gate passed. Its container job subsequently failed at the large
Board native capacity check described below. Do not close dependent PRDs until
the repaired images have actually completed the required gates.

### Phone Board edge-scroll capacity failure

At `f3daad3`, run `37547634517`, container job `112558720128`, the desktop
Board capacity scenario passed and the 390 px scenario failed at
`board-capacity.case.ts:260`: the predicate seeking a later empty column's
drop center in the middle half of the viewport stayed null for five seconds.
The retained screenshot shows later empty columns visible while the source
Card remains pressed in the page snapshot. This establishes an actual native
failure. The retained trace then shows observations at 85,921, 86,043, 86,359,
86,890, 87,928 ms and progressively one-second intervals, while screencast
frames show a real empty-column drop center crossing the middle half of the
phone viewport between samples (including the frame at 86,245 ms). The fixture
now observes each animation frame for at most the same five seconds and retains
the first eligible empty column beyond the original mounted buffer. It does not
change scrolling or invoke a synthetic drop. The original identity, visible drop
surface, middle-half geometry, source retention, mounted-count limit, real mouse
release, HTTP acknowledgment and exact persisted placement assertions remain.
Browser TypeScript checks pass; actual repaired exact-image execution is still
required. Later native acceptance stages did not execute in the failed run,
and its required gate failed.


## Retained release run 35aabef2 failure audit

[Run 37766259462](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37766259462)
is terminal: source quality, PostgreSQL, immutable image build and security pass;
container integration and required CI fail, and release packaging is skipped.
The full browser step ran 3.1 hours with 288 passing and 17 failing cases. All
seventeen retained failure traces and source locations were inspected; the table
records observed failures, not inferred root causes or completed repairs.

| Scenario family | Failed cases | Observed failure |
| --- | ---: | --- |
| Board invitation administration | 2 | Missing uncertain-issuance notice; cancellation focus |
| Board invitation live history | 1 | Cancellation focus |
| Board lifecycle | 1 | Missing deletion acknowledgment |
| Card copy | 2 | Default title submitted; route assertion prevents expected recovery |
| Navigation observations | 1 | One unexpected navigation observation |
| Organization deletion completion | 1 | Observer status differs before scoped Worker starts |
| Organization departure | 1 | Sole-owner control focus times out |
| Organization invitation administration | 2 | Issuance acknowledgment observation |
| Organization settings | 2 | Current-settings settlement not observed |
| Recipient Board authority | 2 | Stale acceptance action remains visible |
| Recipient Portal live invitations | 2 | Expected live status not observed |

The copy traces independently confirm the attempted body retains the default title.
The [copy admission repair](../card-copy.md#executed-copy-draft-admission-and-capacity)
passes both local native cases; the other fifteen failures remain unresolved in
this audit. No failure is skipped, automatically rerun to erase evidence, or marked
fixed from elapsed time. Later main runs remain independent and require their own
terminal acceptance. Product source is unchanged from the retained revision to
`3656fa63`; later expanded tests and local passes do not make this failed gate green.

The retained copy-capacity artifact separately proves twenty copies at p95
76.98 ms under its documented serial-client conditions. Earlier successful steps
are scoped evidence, not complete release acceptance. The strict
[read/permission ordering matrix](prd-17-acceptance.md#executed-read-and-permission-withdrawal-ordering)
adds sixteen actual races while preserving prior watch/activity ordering, but does
not substitute for native release failures or the full required gate.


## Board invitation admission follow-up for retained 35aabef2 failures

The [Board admission correction](../board-invitation-release-evidence.md#board-creation-and-history-admission-after-real-source-frames)
addresses three more retained failures locally: administration desktop issuance,
phone cancellation and desktop live-history cancellation. All four desktop/phone
cases pass together in 2.6 minutes; the initial local baseline passed all four,
while partial strengthening reproduced phone issuance with a filled email later
cleared and no POST. The final fixture requires actual scoped head/read admission
before composition and after reload, and actual issuance frames followed by
protected history reads before consent. Focus checks admit single activations.

Four new observer regressions pass with seven existing checks; TypeScript passes.
All original key/body/identity/revocation/history, focus, acceptance, preferences,
reconnect, no-reload/no-observer-write and accessibility assertions remain. Overall
administration setup allowance now includes its explicit scoped Worker, while
interaction observation deadlines remain unchanged. No product invalidation or
permission guard is weakened. Owned fixtures are removed, original services/data
preserved. This is local optional-verification Production/restricted PostgreSQL
proof. Twelve other retained failure cases remain without a local repair in this
audit; the original seventeen-failure release run remains failed. Full current
immutable-image execution still governs acceptance and issue closure.

## Automatic Organization metadata routing in native browser acceptance

The retained release phase enabled recipient/issuer invitation discovery while
leaving Organization metadata discovery disabled from the earlier transaction
fixtures. The metadata delivery script correctly restores that earlier isolation;
the full browser phase must enable its own automatic metadata routing. It now
sets and persists `STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=true`, recreates
the same retained Worker image and asserts its effective flag. Scoped browser
Workers inherit the phase through `GITHUB_ENV`; earlier isolation stays intact.

An isolated native baseline with metadata disabled and invitation authority
discovery enabled had two passes/four failures in 4.6 minutes. Both settings
cases failed at version/read settlement; invitation administration failed once
at member-added delivery and once at preference focus. Enabling actual automatic
metadata delivery, with no explicit Organization scopes, produced five passes
and one phone preference-focus failure in 3.4 minutes. Both settings tests were
unchanged. The phone trace shows the acknowledgment action disabled when focus
was attempted. The retained departure trace separately shows the link keypress
staying on Organization Home before waiting for a missing departure control.

Invitation composition/reload and departure navigation now require the real
actor/scope-bound stream head and a successful protected read started after it.
Enabled focus admits one keyboard action; the preference test establishes that
focus before changing the profile and still requires its preservation afterward.
The final invocation passes all six desktop/phone settings, invitation and
departure cases together in 3.3 minutes. Settings assertions and all original
mutation counts, version/key/body retries, later membership preservation, Portal
separation, actual source delivery, focus, reload and accessibility checks remain.
Browser TypeScript, eleven observer regressions and routing shell syntax pass.

This is current compiled Production API/MUI, separate real Worker, Nginx and
restricted PostgreSQL schema-112 evidence, with the optional-verification browser
policy and disabled mail providers. Owned containers/database are removed and the
three original services/data preserved. Five more retained failures are addressed
locally; seven other retained failures still need repair/verification. The original
release run remains failed. Full current immutable CI and release-bundle acceptance
remain mandatory; this local invocation does not close PRD-03, PRD-60 or ARCH-11.

## Current Board membership authority and actual recipient interruption

All four retained recipient cases were reproduced locally: desktop/phone Board
issuer downgrade kept stale acceptance, and desktop/phone Portal recovery did not
observe interruption after only changing browser offline state. The unchanged
Board producer emits `BOARD_MEMBER_ADDED` / `BOARD_MEMBER_ROLE_CHANGED`; the
PostgreSQL authority publication and source view still admitted only legacy
`BOARD_MEMBER_UPDATED` / removal. The routing regression fails before the repair
on missing member-addition routing. This is a product delivery gap, not a weakened
browser assertion.

[Migration 113](../../db/migrations/113_invitation_recipient_membership_authority.sql)
adds current membership families to the publication and private source view while
retaining legacy updates, Organization lifecycle/account source branches, canonical
references, fixed-cutoff 100-candidate paging and leased delivery. Original source
IDs and historical events are not rewritten. API/Worker require its named ledger
entry. The new PostgreSQL regression covers all three upsert families, discoverable
jobs, restricted Worker delivery, one recipient revision per source, retry without
duplicate effects and unchanged invitation-transition/Work-readiness journals.
It passes alongside the existing 205-candidate, 100/100/5-page rollback/private
capability contract. Clean/repeat/forward-upgrade, concurrent migration runners,
failed migration rollback and unrecorded-migration rejection pass through 113.
Restricted API/Worker readiness also passes missing-entry refusal and recovery.

The Portal test now explicitly closes its actual server-connected socket during
the browser offline interval and blocks reconnection until a genuine invitation
is created by the independent issuer. Original messages are forwarded unchanged
in both directions and observed only for the page's Watch invocation. No event,
HTTP response, cursor, database readiness or application state is manufactured.
Existing interruption deadline, three original source IDs/sequences, exact private
envelope keys, missed-event replay, current acceptance, focus, no document reload,
logout withdrawal and accessibility assertions remain.

The final native invocation passes all four cases together in 2.3 minutes. Board
browser assertions were unchanged; actual issuer downgrade removes stale consent
and protected acceptance is rejected. Strict zero-warning Release build, browser
TypeScript and shell syntax pass. This is frozen current compiled Production
API/Worker, MUI bundle, Nginx and restricted PostgreSQL schema-113 proof, with
optional-verification browser policy and mail providers disabled. Owned fixtures
are removed; original services/data remain. Four more retained failures are
addressed locally, leaving three other retained failures to repair/verify. The
original failed release gate remains failed. Current immutable CI, complete native
suite and release-bundle verification still govern issue closure.

## Archive source admission and navigation completion boundaries

The remaining seven-case baseline passes both Organization deletion-completion
cases and all three navigation widths, but fails desktop Board deletion before
any DELETE is dispatched. The phone Board case passes. The older retained
release trace separately has one browser DELETE with its deliberately lost
response; the HTTP 200 belongs to that original intercepted request's
`route.fetch()`. Its later retry keypress sends no second browser DELETE. Those
are distinct observations, not proof of a malformed persisted deletion receipt.

The Board fixture now passively observes the actual Organization Watch invocation,
actor/scope envelope and target Board sources. A successful archive-directory read
must start after the observed head/change before consent or receipt recovery is
admitted. Re-archive and deletion each require their own canonical event and
subsequent read. Failed, older, foreign and unrelated reads/sources cannot satisfy
the boundary; replayed event IDs count once. Fourteen observer regressions pass.
Keyboard actions establish enabled focus before one activation; only focus can
be retried. Original mutation counts, keys/bodies/versions, privacy withdrawal,
independent deleted-parent rejection, child lifecycle, focus and WCAG assertions
remain required.

Navigation waits for each acknowledged screen's current-account confirmation to
remove its own retained original before leaving that scope. The deliberately lost
Card original survives leaving and must return with its original key/query/event;
the final zero-retained-original assertions remain. No storage entry is removed by
the fixture. Lifecycle observation now qualifies frames with the actual outgoing
Watch invocation and waits for ACTIVE before deletion and PENDING before taking
the Member offline. Real Worker completion, exact terminal source, account
replacement, reload recovery and logout withdrawal assertions remain.

Browser TypeScript and the observer regressions pass. The final seven-case native
invocation exits zero with all seven cases passing together. It uses the frozen
current compiled Production API, separate real Worker, MUI bundle, Nginx and
restricted PostgreSQL schema 113, with optional browser email verification and
mail providers disabled. All owned containers and the cloned database are removed;
the original three services and data remain. This addresses the final three
failures from the retained 35aabef2 browser audit locally. It does not establish
current immutable CI success; full PRD acceptance, the newly retained capacity
failure below and release-bundle validation still govern closure.

## Retained db4ff033 phone capacity failure

[Run 37803017597](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37803017597)
passes web/.NET/PostgreSQL source quality, build-once images and image security,
but fails container integration's concurrent/large-board rank step. Its large
Board browser pair has one desktop pass and one phone failure at
`board-capacity.case.ts:314`: after horizontal edge scrolling and moving the
pointer back to the middle, the selected empty-column drop target's center is
465.0859375 pixels while the visible right boundary is 366 pixels. This is a
visible-target assertion failure; it does not establish a rank-storage failure.
The final gate fails and the release bundle is skipped, as required.

Artifact `11566071486` retains the phone trace and was downloaded for the next
investigation; its archive SHA-256 is
`c060397d21c6cd3fd35748b21335049fc68884b764e8914c847fc7cb7ffaf3b9`.
The drag assertion and mandatory capacity gate remain intact. Repair and native
verification of this failure are still outstanding. It is separate from the
three earlier retained failures addressed by the seven-case invocation above.

## Stop horizontal edge input before inspecting the observed destination

The retained db4ff033 phone trace keeps the pointer at the horizontal edge while
checking source attachment and mounted-column count after observing a later empty
destination. Auto-scroll continues during those browser round trips. By the time
the center stop input and final rectangle read complete, that same destination's
center is outside the visible canvas. The failure therefore precedes the actual
cross-List release/HTTP command.

The capacity fixture now sends the real center stop input immediately after the
animation-frame observer returns. It settles the gesture, then checks source
retention and bounded mounts. It retains the same canonical destination rather
than selecting an easier replacement. Middle-half observation beyond the original
buffer, visible destination center, actual pointer release, one acknowledged
cross-List command, exact source/destination/sibling state and all keyboard,
version, focus, viewport and archive-history assertions remain unchanged. No
scroll offset, drag state, response or event is fabricated; no assertion budget,
automatic test retry or mandatory gate changes.

A separate unchanged CPU-four baseline passes phone but fails desktop earlier at
`board-capacity.case.ts:77`, while focusing a newly mounted List. It never reaches
desktop horizontal dragging, so it is not evidence that the edge correction fixes
that stress failure. The existing five-second focus admission and 500ms individual
focus attempt remain. The final continuous-Worker check below governs its current
verification.

The first normal-CPU validation passes phone, but desktop times out waiting for
the first keyboard Card move response at `board-capacity.case.ts:125`. Its trace
has no browser Card move POST after the twelve aligned Arrow keys and final
Space; the after-key snapshot has the workspace busy during protected refresh.
Each Arrow already waits for current admission, but the final drop previously
followed the window inventory without the same check. The fixture now requires
current workspace admission, enabled focus and the same still-active source
before its one Space drop. It retains all twelve targets, two-pixel alignment,
same source/version and original persisted-placement assertions. No drop key is
retried. The next normal-CPU invocation passes that keyboard stage but fails later
horizontal transfer; the complete-history investigation below records that result.

Browser TypeScript and rank-script syntax pass. Exact retained-image CI and
complete PRD acceptance remain required before closure.

## Board delivery readiness follows the complete bounded history

The subsequent normal-CPU invocation with final keyboard admission passes phone;
desktop passes the keyboard Card move and reaches horizontal transfer, then times
out waiting for its response. This is another failed invocation, not a completed
capacity result. Its retained HTTP trace contains three readiness responses with
100 events, `hasMore=true`, `pending=false`, `resetRequired=false`. The shared
`waitForBoardDelivery` helper previously accepted each first page as complete.

The production `WorkEventReadWindow` contract checks a bounded ordered window,
not every published source. A ready first page can have more history and a later
pending source. Its consumer must follow the cursor; repeatedly checking only
the first hundred cannot establish readiness of later accepted commands. The
helper now resumes from the actual cursor until a final non-pending page, retaining
observed-source admission across pages. A reset retires that admission; malformed,
backward or inconsistent progress fails. Every read still uses the authenticated
Board sync endpoint, and the existing 30-second deadline remains. No Worker job
or event readiness is fabricated and no private cursor enters diagnostic output.

Four regressions cover a ready first hundred followed by a pending source, an
empty head, reset recovery and invalid progress. They join the mandatory web
quality step; all 22 Board-read, invitation/archive-observer and delivery-admission
checks pass, as does browser TypeScript. The complete current-schema native
capacity results below verify both browser widths and the post-browser full
fingerprint of 100,000 archived records. Current immutable CI and complete PRD
acceptance remain outstanding.

The complete-tail invocation correctly rejects desktop setup after 30 seconds
at the initial delivery wait; phone subsequently passes. It no longer admits an
incomplete first window as ready. The rank fixture previously force-recreated its
Worker after publishing all bulk commands. Background jobs retain their existing
two-minute lease on interrupted processing, so that restart can leave accepted
sources pending beyond the unchanged readiness deadline.

The rank fixture now establishes its scoped real Worker after Organization
creation, before creating the Board or publishing any bulk Work command. That
same Worker stays alive through the concurrent SQL/HTTP checks and both browser
cases; the existing exit trap restores the ordinary Worker. Readiness still
requires real complete-history delivery, and the 30-second/150-second bounds,
concurrency assertions and 100,000-row archive fingerprint remain. The complete
continuous-Worker normal-CPU native invocation exits zero: both desktop and phone
cases pass together in 2.0 minutes, and the post-browser archived count and full
100,000-row fingerprint match. Concurrent append, relative moves/positions and
non-reapplying durable replay also pass. It uses the frozen current compiled
Production API, separate real Worker, MUI bundle, Nginx and restricted PostgreSQL
schema 113; owned containers/database are removed and original services/data
remain. This is current local runtime evidence, not current retained-image/full
release acceptance. The fresh CPU-four invocation also exits zero: both widths
pass together in 3.7 minutes, including the previously failed desktop List focus,
keyboard moves, pointer moves/cancellation, horizontal transfer and List keyboard
reordering. Its complete concurrent/replay checks and post-browser 100,000-row
archive fingerprint also pass. Both invocations remove only their owned fixtures;
the original three services/data remain. The earlier failures are retained above,
not automatically retried or hidden. Current mandatory immutable-image CI and
complete PRD acceptance still govern issue closure.

## Admit native producer editors after their actual Board source

Retained immutable run [37805991890](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37805991890)
fails the strict verified-account producer phase at the phone reminder option.
The original browser trace shows the option visible at 229319 ms, a protected
Board GET starting at 229346 ms and the reminder panel showing `Checking current
Card access…` at 229370 ms. A second real reminder GET follows. The initial
editor/read and option visibility therefore precede the stream's protected
canvas refresh; focusing the vanished option cannot complete selection. The
fixture now observes the actual outgoing Board `Watch`, its scoped incoming head
and a successful protected Board read started after that head. It also requires
the workspace to be idle before the single admitted keyboard opening. The real
60-second due clock, two native inboxes, Worker firing, original scheduling
receipt, complete stored attribution, private creation/read envelopes and
accessibility checks remain required.

A fresh current-schema strict eight-case baseline passes both reminder widths,
both selected-teammate widths and desktop confirmed groups, but fails both
assignment widths and phone confirmed-group retry focus: **5 passed, 3 failed in
9.1 minutes**. These failures remain retained separately from the release trace.
Desktop assignment sends its deliberately replaced original PUT but no browser
retry; phone assignment reaches reassignment and later loses the author option.
No duplicate-notification conclusion follows from those failures. The phone group
case loses automatic retry focus; that assertion remains required.

Assignment editor admission now follows actual `CARD_MEMBER_ADDED` and
`CARD_MEMBER_REMOVED` frames and a successful Board read started after the latest
source, with the expected current Card revision and idle workspace. This includes
the original-key retry boundary. The passive existing history observer accepts
an explicit protected read path, preserving the invitation-history default.
Its regression rejects invitation collection reads for canvas admission,
pre-source reads, denied reads and duplicate source counting. Confirmed-group
setup likewise waits for its actual head and protected canvas read before drafting.
These are observation barriers, not replayed activation keys or synthetic delivery.
Browser TypeScript and all **23** read/source/delivery observer regressions pass.
The fresh corrected invocation passes **all eight cases in 9.7 minutes**, including
both original-key assignment recovery widths, both actual due-fire widths, both
automatic confirmed-group retry-focus checks and both unchanged selected-teammate
cases. No activation key is retried by the fixture, no timeout is widened and no
notification/source/delivery row is fabricated. The private scheduling and command
receipts, stored notification attribution/clocks, live event identities, role
withdrawal, consent/quota, tagged Axe and overflow assertions remain intact.

Execution uses the frozen current compiled Production API, separate real Worker,
MUI/Nginx and restricted PostgreSQL 17/pgvector schema 113. API and Worker both
require verified email; each new fixture login is refused before verification and
accepted afterward. Owned containers/database are removed and the original three
services/data remain. This establishes current local runtime evidence, not current
retained-image/full CI or email-provider delivery. Complete PRD acceptance and
current required release gates still govern closure. The separate retained run
[37808888458](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37808888458)
fails at the large-Board horizontal transfer response wait, rather than this
producer phase; the preceding continuous-Worker capacity repair has local normal
and CPU-four evidence, while its current immutable verification remains pending.

The later retained run
[37811393903](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37811393903)
also stops in this strict producer phase: **7 passed, 1 failed in 7.6 minutes**.
Its phone reminder fails the option-visibility assertion at the old line 133,
before interval selection. Those fixtures predate the current source-admission
repair in `4c436df7`; the failed immutable run remains failed. The current eight-
case local pass does not substitute for the still-running current exact-image gate.

Run [37824613470](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37824613470)
at `4c436df7` has now failed before that strict producer phase, in the large-Board
rank stage. Its desktop capacity case passes; the phone case fails the destination
drop-target viewport assertion at `board-capacity.case.ts:317` with ratio zero
(one passed, one failed in 1.6 minutes). The retained capacity artifact is
`11573234558`, SHA-256 `aac3a6b49f27c6e89582720bef240bf666841fd52f3fabb5ccf196ac5eaf8170`.
Its trace records the edge input followed by destination observation, then the
center stop input and failing viewport assertion. Root-cause/repair verification
is still required; earlier normal/CPU-four local capacity passes do not establish
this immutable gate. Required CI fails and the release bundle is skipped.

The retained phone trace narrows the failure further. At destination observation
its canvas bounds are x=24..366, the admitted empty drop center is x=145.695,
and `scrollLeft` is 65855. The center input is x=195. Subsequent recorded snapshots
show offsets 65733, 65367, 65156 and finally 64945: 910 pixels of additional
leftward movement from the original observation before the stop settles. The
original viewport assertion is retained. This is evidence of overshoot, not yet
a verified explanation of whether auto-scroll, browser focus or layout caused it.

A fresh numeric-only local diagnostic invocation passes both original desktop
and phone cases under CPU-four throttling (2 passed in 2.9 minutes). Wrapped
scroll calls record 143 horizontal calls on desktop, maximum step 9.380 pixels,
and 123 on phone, maximum step 8.246 pixels; neither invocation records a step
over 20 pixels. Concurrent rank allocation, durable replay and the full
100,000 archived-row preservation checks also pass. The preceding diagnostic
invocation fails both cases because instrumentation incorrectly references a
test-runner clock inside the browser; its measurements are invalid and retained
separately. The corrected instrumentation uses the browser clock and is removed
from the repository after diagnosis. No timeout, destination identity, input
gesture, viewport assertion or persistence check is relaxed.

This uses the frozen compiled Production API/separate Worker and MUI/Nginx on
restricted PostgreSQL schema 113; it does not establish the newest schema-114
source or current exact retained-image release. Owned fixture services/database
are removed and the original three services/data remain. The immutable phone
failure is still unresolved. A reproduction capturing the large jump or stronger
retained geometry evidence is required before choosing a product repair.

## Bounded numerical capacity scroll evidence

The capacity cases now attach `capacity-scroll-diagnostics` JSON to their existing
Playwright artifact on success or failure. The browser installer records owned
Kanban `scrollBy`, direct horizontal assignments and `scrollIntoView` calls with
numeric arguments, prior offsets, pointer coordinates and browser-clock times.
It retains at most 2,048 samples and reports discarded sample count. No entity
identities, content, route, cookie, credential or native option text is retained.
It does not poll geometry, force scrolling, fabricate input or decide admission.
The original test gestures, target identity, timeouts, viewport/windowing and
persistence assertions remain required. The diagnostic can distinguish a large
auto-scroll argument from a direct assignment or focus-scrolling operation in
the next immutable failure; it is not itself a product repair or a performance
benchmark. Instrumentation overhead remains a limit of diagnostic evidence.

Five mandatory Node regressions verify both native overloads, receiver/argument
and return/exception preservation, direct assignment/focus forwarding, privacy,
bounded retention, idempotent installation, diagnostic-read failure isolation and
single native evaluation of option accessors. Browser TypeScript also passes.
These are helper/source checks, separate from real browser and release gates.

A separate CPU-eight diagnostic invocation fails the desktop protected-read
admission (one observed read rather than two within five seconds) and the phone
destination observer (no admitted original empty target within five seconds).
The latter records 111 horizontal scroll calls, all at most 8.246 pixels, with
late calls spaced roughly 47–64 milliseconds apart; the final observation is
`scrollLeft=65997`, nearest eligible center x=3.695 outside the middle half.
It does not reproduce the retained large-step hypothesis. Both failures remain
retained and neither timeout nor assertion is widened. Its owned fixture services
and database are removed. This diagnostic stress is distinct from the original
retained viewport failure and does not certify normal-condition performance.

Earlier exact-image run [37820639326](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37820639326)
at `f05db23f` passes both capacity cases (1.8 minutes), then fails the strict
producer phase (7 passed, 1 failed in 7.7 minutes). The reminder's due-time option
is not enabled at the old `card-reminders.spec.ts:133` admission boundary; this
revision predates `4c436df7` source-admission repair. Its required gate fails and
release bundle is skipped. Its capacity pass does not erase the later retained
phone failure or establish the current complete release gate.

The bounded installer passes both unchanged desktop/phone capacity cases at
CPU-four in a fresh invocation (2 passed in 2.9 minutes), with the same frozen
Production API/Worker, schema-113 restricted PostgreSQL and complete rank/replay
and archived-row checks. An independent native Chromium artifact proof passes
(1 case in 1.2 seconds): actual DOM scroll assignment, `scrollBy(-211, 0)` and
focus scrolling generate the three expected numerical samples and JSON persists
in Playwright's output folder. The case writes the attachment using
`info.outputPath`, so the existing `test-results/board-capacity` upload retains it
independently of HTML-report body attachment storage. This focused proof does
not exercise authentication, Worker transport or current immutable release.
Owned capacity services/database are removed and original services/data remain.
Current exact-image validation and the original phone defect are still pending.


## Retained immutable-image run 37999375942, 2026-10-10 inspection

This run tests commit `b9f397836e759bb41cbfe47fbd3d2095c24eee80`, not current
main. Fresh terminal metadata and decoded job logs establish these failures:

- Browser-notifications job `114079086668`: both Board-member viewport cases
  exceed their original 90-second budgets in Worker restoration at the test's
  finally block. Earlier assertions cannot be inferred from the timeout alone.
- Full shard 1/4 job `114079086680`: phone Board-member lost-response recovery
  does not display the expected uncertainty message; desktop Board metadata
  cannot fill the missing dialog field before its original 120-second deadline;
  phone Card-label move selection is disabled at its enabled-control assertion.
- Full shard 2/4 job `114079086672`: phone checklist collaboration cannot admit
  Manage items after revision-4 conflict recovery; desktop Board invitation
  deadline recovery never observes its held command after the eight-second
  profile gate.
- Subsequent terminal full shard 3/4 job `114079086657`: **78 passed and 1 failed**
  in 48.0 minutes. The desktop Organization-deletion recovery case receives the
  actual pending lifecycle frame, but the second client's expected pending-status
  element is absent at `organization-deletion-retries.spec.ts:97` within the
  original five-second assertion budget. This establishes the observed missing
  status, not its cause or a repair. Later assertions in that failed case remain
  unproven. The phone case's pass does not substitute for desktop recovery.

Foundation job `114079086713` and security pass on that same old candidate;
these do not establish whole-run success. At the subsequent shard-3 inspection,
full shard 4/4 remains live; the separately recorded commands gate below passes.
The failures remain retained; no assertion,
per-case deadline, role/state matrix, retry policy or mandatory CI dependency is
weakened. The newer compiled-source/native results are separately scoped in
[primary-screen reflow](primary-screen-reflow.md) and
[modular-monolith acceptance](arch-03-acceptance.md). They do not replace these
failed immutable-image scenarios or prove their causes. Current-head CI remains
required before release acceptance or closure.


The unchanged complete invitation-creation deadline file now passes **4/4**
on current schema-133 compiled API/Worker and the final long-label frontend,
in **124,693.767 ms**, one result per original Organization/Board and desktop/phone
case, zero skipped/flaky/unexpected outcomes or report errors. Optional email
verification matches this full-browser fixture policy; strict provider acceptance
remains separate. The separate Worker uses actual automatic discovery in the
fresh restricted PostgreSQL database. Original aggregate deadline, eight-second
profile gate, 7,001 ms command hold, identical original key/body, single committed
invitation, fresh-admission recovery and Axe assertions are unchanged. Owned
containers, database and credential environments are independently absent.
Private report: `invitation-deadline-schema133-current-native-20261010/report-private.json`.
This does not reproduce the old immutable-image failure or prove its cause; no
speculative product/fixture repair is made. Current immutable-image acceptance
and the separate checklist collaboration investigation remain required.


The unchanged complete checklist collaboration file subsequently passes **2/2**
on current schema-133 compiled API/separate scoped Worker/final frontend,
in **105,148.847 ms**, one result per original desktop/phone case and zero
skipped/flaky/unexpected outcomes or report errors. It preserves both real
clients, submitted contributor conflict with server 409, dirty text/completion,
revision-4 reopening, actual socket outage/reconnect and missed completion,
real committed/lost-reply mutation, membership revocation, denied original-key
replay/new writes and exact unchanged owner-visible content, keyboard and Axe.
Optional email verification is this fixture's original policy. Owned containers,
database and credential environments are independently absent. Private report:
`checklist-collaboration-schema133-current-native-20261010/report-private.json`.
This scoped local pass does not establish the old immutable failure's cause or
full Checklist/release acceptance. The three original Board-member, Board-metadata
and Card-label files now run independently on current compiled backend/frontend,
with strict verification and unchanged scenario assertions/deadlines.


The subsequent unchanged complete Board-member/metadata/Card-label phase passes
**6/6 on their only attempts**, zero skipped/flaky/unexpected outcomes or report
errors, in **299,464.072 ms**. Original conflict and committed/lost-response
recovery, exact request/count/state checks, keyboard focus and geometry/assertion
budgets remain. It uses the current final frontend with compiled schema-133
publication-backend API/Worker (before the later Demo queue audit repair), fresh
restricted PostgreSQL, Nginx/CSP and strict verified-account fixture policy.
Owned containers, database and credential environments are independently absent.
Private report: `board-ci-failures-schema133-current-native-20261010/report-private.json`.
Together with the separately executed 4-case invitation and 2-case checklist files,
this covers the five original source files containing the observed old browser
failures. These were separate complete invocations, not one combined 12-case run.
All retain single attempts and original deadlines. None reproduces the old
immutable failures or proves their causes; current exact-image/full-release
acceptance remains required. No fixture or product admission is weakened on
that inference. PRD-04 stays open at **15%**, PRD-05 at **15%**, PRD-10 at **35%**
estimated work remaining (planning estimates).


## Retained immutable commands proof for schema 131

Job `114079086712` in run `37999375942` subsequently completes successfully
on commit `b9f397836e759bb41cbfe47fbd3d2095c24eee80`. Its actual 117-step metadata
and decoded log were inspected against that exact commit's
`integration-suites.json`, rather than current main. Of the manifest's 86
commands-scoped steps, **83 execute successfully**. The remaining three are
Capture container state and logs, Prepare Upload integration diagnostics and
Upload integration diagnostics; all three are explicitly `if: failure()` in
that candidate's workflow and correctly skip on this successful job. Other
skipped steps belong to separate browser suite scopes. No required commands
execution is missing or unexpectedly skipped.

Successful steps include archive checksum/load and embedded identity verification,
startup/configuration refusal, actual profile/authentication/registration/invitation
commands, Board grants/admin/copy/member receipts, automatic recipient and issuer
Worker routing/restart, terminal search/inbox withdrawal, labels/assignments/watch/
mentions/movement/copy/dates/attachments/checklists, atomic rollback/replay,
leased Worker readiness and fixed-capacity lifecycle/notification/deletion gates.
These are executed older-candidate steps, not a new count of individual test cases.
That candidate's highest migration is **131**; it does not prove migrations
132/133, the later Demo publication/audit repairs or current frontend. Three
browser jobs on the same run remain failed, so successful commands/foundation/
security do not establish whole-run `required-ci`, release bundle or current
main acceptance. Current immutable-image CI and full original ticket criteria
still govern closure.
