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
editor, viewing-timezone/Board override behavior, reminder
interval selection, scheduling/rescheduling/cancellation or delivery. Those
requirements, two-client browser evidence and performance/accessibility checks
are still required before closure. PRD-17 also remains open.

Initial Linux source CI found two fixture issues: the disposable storage role
needed SELECT plus UPDATE on the five date columns, and a 2026 Vancouver fallback
assertion was outdated. B.C. adopted permanent daylight time after March 8, 2026
(https://news.gov.bc.ca/releases/2026AG0013-000209). The suite now checks the historic
2025 25-hour day and the current 2026 24-hour November day. Production timezone
conversion already followed the runtime's current timezone database correctly.
Only the disposable role receives the additional column permissions, inside the
fixture rollback transaction. Fresh CI remains necessary.

The date storage write also invokes the existing canonical Card route trigger.
A subsequent CI pass exposed the fixture role's missing card_routes permissions.
The disposable role now has SELECT/INSERT/UPDATE for that routing table within
its rollback transaction, matching the trigger's ordinary runtime requirements.
The Linux domain and host suites passed on d595cbd; storage verification remained
failed until this additional fixture correction. Production grants are unchanged.

## Card detail display

The MUI Card detail panel now shows optional start/due values and textual/icon
urgency: UPCOMING, DUE_SOON (within 24 hours), DUE_TODAY (viewer calendar day),
OVERDUE, or COMPLETE. Completion takes precedence; actual expiry precedes the
calendar-day label. UTC comparisons retain microseconds. The bounded current
profile read supplies the configured viewing timezone and locale; the stored
timezone context is shown separately. Date-only due display omits the time.
Profile checks refresh while visible, on focus and every 30 seconds. Date scope
loss, account changes and malformed profile/date data hide dates; late profile
responses are fenced by component scope. Cards without dates add no profile read.

The date model and display have 16 passing local tests; typecheck/lint/build
passed. Two browser scenarios (1280px and 390px) are collected for exact-release
CI: separate authenticated sessions, canonical completion, configured timezone
changes and clearing dates. They explicitly refresh canonical state and do not
prove realtime notification delivery. Runtime browser evidence is pending.

The editor, Board timezone override, reminders and wider acceptance remain open.
