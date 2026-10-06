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
