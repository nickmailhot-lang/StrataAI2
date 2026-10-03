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
