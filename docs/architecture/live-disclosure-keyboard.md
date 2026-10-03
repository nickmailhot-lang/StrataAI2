# Live assignee disclosure and keyboard verification

Exact-image CI run 37095618475 completed with 76 browser cases passing, two
failing and one identity-mail case skipped in the general suite (identity mail
has its separate mandatory stage). The failures were a desktop label removal
and opening the phone assignee disclosure during live snapshot re-admission.

The retained traces show no DELETE request for the failed label removal. The
keyboard action resolved its option while disabled, after the earlier enabled
assertion; a Board snapshot response occurred immediately before the action.
The phone disclosure trigger was keyed with Card revision/access content and
could be replaced during keyboard activation. The final snapshot showed the
disclosure closed and an unrelated Card-save status.

The assignee disclosure trigger now lives outside the revision/access-keyed
content. It retains its DOM identity and keyboard focus across live refreshes.
Users can express show/hide intent during an access check; the section shows
only a checking status, makes no protected read while unavailable, discards old
names and reads the current revision once admitted. Changing Card scope still
resets the disclosure. The label browser fixture waits for existing Worker
delivery before navigating to label choices, so its next keyboard action is
not racing its own undelivered setup/mutation events. Original keyboard and
focus assertions remain in place; no retry or budget waiver was added.

Local verification: 22 assignee-disclosure/label-picker tests passed, including
stable trigger identity/focus, no read while access is unconfirmed, retained
disclosure intent and rejection of stale/foreign responses. Type checking and
lint passed. Three affected browser cases were discovered; repaired browser
execution is still pending exact-image CI.

The performance artifact for the same failed overall run independently records
the unchanged budgets passing: 50 dated Cards across three Lists became usable
in 1005.82 ms (1500 ms budget), cached detail in 141.30 ms (200 ms), and 20 date
mutations had p95 50.72 ms (500 ms). Normal Board readiness/detail/mutation and
Card/List feedback budgets also passed. These are measured stage results, not
an overall green release or complete PRD acceptance.

## Checklist disclosure follow-up

Runs 37097898694 and 37098456354 also failed the general browser suite. The
latter reported missing checklist item content and a missing renamed checklist
after keyboard disclosure. The checklist root trigger is now stable and accepts
show/hide intent during re-admission without making a protected read. Expanded
item IDs live at Card scope, so a live revision refresh retains expansion intent
while discarding old content and fetching current summaries/items. Explicitly
hiding all checklists clears child expansion; changing Card scope resets it.

Local verification: 47 reader/parser tests passed, including stale-content
removal and retained expansion through re-admission. Type checking, lint and
production build passed. Six affected browser cases were discovered; their
execution remains pending exact-image CI. The peer-session creation, completion
and ordering cases now assert refreshed item content without reopening an
already expanded disclosure. No acceptance assertion or timeout was waived.

## Returned focus and filter opening

Label assignment and personal Reminder save/recovery controls now retain their
returned-focus intent across subsequent temporary access checks. They restore
focus only when it is on the page body or the same action, and clear that intent
on an intentional focus transfer. They do not repeat a command automatically.

The generic filter opener accepts keyboard intent while admission is pending.
Its dialog shows a checking status and defers identity/choice reads until the
Board is admitted. Deferred opening is scoped to the original Organization and
Board and is canceled by closure or scope changes. All protected filter actions
still require current admission.

Local verification: all 62 label-picker, Reminder and filter tests passed,
including returned-focus preservation, deliberate navigation, deferred read and
scope/closure cancellation regressions. Type checking, lint and production build
passed; five affected browser scenarios were discovered. These repairs still
require execution against their exact release images. They do not establish that
the other reported drag, option activation or notification failures are fixed.

Run 37099065647 completed with 75 passing, five failing and one skipped general
browser cases. The phone Reminder case failed to load its initial personal
choice during live updates; the opener previously rejected actions while
admission was pending. Its generic opener now accepts
opening intent while admission is pending, shows a checking status and defers
private account/reminder reads. Explicit closure and Card scope changes cancel
that intent. Reminder mutation/recovery controls retain their existing admission
and original-command checks. All 29 Reminder tests and production build passed
locally; type checking and lint passed. Exact-image browser execution is pending.
The same run's initial archived-Card deletion-review focus failure is still under
investigation; it is not a deletion-command or backend acceptance failure.

The retained archived-Card trace shows the review trigger disabled at the first
focus assertion, then enabled but inactive. Opening deletion review now retains
only the requested Card ID through the ongoing archive read. The trigger stays
focusable; a checking dialog offers cancellation and contains no confirmation or
old Card details. Once admitted, the current entry and deletion capability must
still exist before displaying the fresh review with unchecked consent. Closure,
denial and disappearance cancel the pending review. The mutation guard and
explicit confirmation remain unchanged. All 33 archive component tests passed,
including four new deferred-review regressions, plus type checking, lint and
production build. Two browser cases were discovered; exact-image execution is
pending, so the archived-Card failure is not yet declared resolved.

Source CI run 37104159265 caught a new regression-fixture setup race: the test
invoked its mocked live invalidation before subscription initialization. The
fixture now explicitly waits for that subscription before sending an event.
No permission or consent assertion was changed. A full default local run passed
862 tests and timed out one existing Board due-status assertion while still
loading. The unchanged full suite then passed all 863 tests across 65 files with
two workers and the subscription correction. The failed CI run produced no
release bundle; the correction still requires fresh source and image CI.

Run 37104084980 also failed source tests (862 passed, one failed), in an existing
label creation recovery fixture that asserted its passive recovery callback
immediately after discovering the retry button. That fixture now waits for the
same callback assertion before retrying; original body/key and disabled-cancel
assertions remain unchanged. All eight label creation tests pass locally. The
prior full 863-test pass precedes this test-only synchronization correction;
fresh full source/image CI remains authoritative.

## Notification recovery follow-up

Runs 37099704590 and 37100150518 finished with six browser failures each (76 and
78 passing general cases respectively). The former's retained notification
trace records a focused Mark read button followed by a separate page-level key
press, but no notification-read POST. The fixture now sends Enter through the
current named control, retaining keyboard activation and all original receipt,
privacy, canonical state, focus and revocation assertions. It does not retry a
write blindly or waive a timeout.

The notification page now returns focus to its original-command retry after
response loss, preserves retry/refresh focus through subsequent inbox reads and
respects deliberate navigation outside the inbox. It still removes protected
inbox content during each read and revalidates the actor before writes; focus
changes never submit a request. All 15 notification component tests passed,
including two refresh/navigation regressions; type checking, lint and production
build passed. The affected real-image browser case was discovered, not executed.

Run 37100787880 (assignee disclosure repair) finished with 79 passing, seven
failing and one skipped general browser cases. Both label cases timed out in
the newly added delivery barrier: their fixture never selected its disposable
Organization for Worker delivery. Each label case now owns that scoped Worker,
waits for its initial setup delivery before opening the Board, and restores the
prior Worker scope in `finally`. The 30-second delivery assertion and all label
keyboard/receipt/filter/deletion assertions remain unchanged. Two cases were
discovered; repaired image execution remains pending. Checklist disclosure and
Reminder failures in that run have later main repairs pending execution.

Source stages for commit 4522155 passed web/.NET/PostgreSQL, immutable image
build and security in run 37104844850; its container integration is still live.
These stage successes do not prove an overall green release or later commits.
