# Card dates foundation — PRD-12

`PATCH /cards/{id}/dates` accepts `startAt`, `dueAt`, `dueTimezone`,
`dueHasTime`, `dueComplete` and the current Card `version`. A UUID retry key uses
the existing command receipt store. The response contains `{ card, changed }`.
Both dates are optional. Clearing both dates clears context; completion and the
timed flag require a due date. Start may not follow due.

Explicit times must be UTC ISO instants. Calendar dates use the supplied IANA
timezone (`UTC` is also supported). A date-only start is the first valid instant
of that local day; a date-only due is the final PostgreSQL microsecond of that
day. DST therefore produces 23-, 24- or 25-hour days. Entirely skipped local
dates are rejected. Stored date-only UTC values may be roundtripped unchanged
when changing completion. Other timed instants require `dueHasTime: true`.

Migration 036 adds fields to canonical Cards, retaining forced tenant RLS and
existing identity references. Snapshot, archive, filter, assignment, label and
mutation readers retain dates; List copies copy dates into new Cards. Personal
reminders are not part of these fields and must not be copied as personal state.

The owning Organization and Board locks precede current account, membership,
Board, List and Card admission. Current Card revision is checked even for a
no-op. A canonical no-op leaves Card revision and publication untouched. A
changed date update advances the Card once, emits `CARD_DATE_CHANGED` and/or
`CARD_DUE_COMPLETED` / `CARD_DUE_REOPENED`, and publishes relevant current watcher
intents in the same transaction as audit, event readiness jobs and retry receipt.
Completion leaves lifecycle state independent. Clearing a completed due emits
the date change without inventing a reopening of an absent date. Replay requires
current active parent scope and current editing authority, preserving privacy
after archive or membership removal.

Tests cover UTC precision, calendar dates, DST, invalid timezone/order/flags,
skipped dates, no-op, completion/reopening, clearing, private replay and watcher
publication. The PostgreSQL storage fixture checks date constraints and RLS.
The exact-image fixture additionally revokes notification insertion to verify
whole-command rollback, retries the original key, and checks date retention
through unrelated edits and archive/restore. This fixture is mandatory in CI.

Local validation: strict .NET build, web typecheck/lint and 29 notification-parser
tests passed. Local .NET test execution is unavailable under Windows Application
Control; Linux CI executes the host and domain suites. Database/container checks
require CI on the exact release images. These checks are pending for this commit.

PRD-12 remains open. This dependency slice does not yet provide the MUI date
editor/status display, viewing-timezone/Board override behavior, reminder
interval selection, scheduling/rescheduling/cancellation or delivery. Those
requirements, two-client browser evidence and performance/accessibility checks
are still required before closure. PRD-17 also remains open.
