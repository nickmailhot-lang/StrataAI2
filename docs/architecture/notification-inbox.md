# Authorized notification inbox (PRD-17)

Assignment and configured watch activity share this inbox. See
[watch activity notifications](watch-activity-notifications.md) for the event-time
producer, stored type values, dedupe and forward migration 035.

The internal app can read its signed-in recipient's notifications for an
Organization through `GET /organizations/{organizationId}/notifications`. The
response contains up to 50 items, newest first, and a UTC timestamp/UUID seek
cursor. Recipient IDs cannot be supplied by the client to select another inbox.
Items contain notification/recipient/actor IDs, type, the current authorized Card
link and IDs, creation time and read time. No Card content, email, private event
metadata or original command receipt is included.

Eligibility applies before the 51-row storage window: active account and required
verification, active Organization membership, current Board view permission and
active Board/Card/List parents. Archived Organizations retain read access;
deleting Organizations deny it. Notification history is retained after departure,
unassignment or archival, while inaccessible records are hidden. Restored access
may reveal retained history, without restoring Card assignments.

An owning Organization transaction takes the Organization and current membership
SHARE locks, obtains the bounded eligible window, then locks its Boards in
PostgreSQL UUID order before acquiring the account/session locks. Fresh joined
eligibility reads after all Board waits protect against revoked access or archived
parents during admission. A changed planned window is masked as unavailable;
a fresh request re-queries eligibility before the limit. New notifications that
arrive during a page read are recovered by refreshing from the first page.

`POST /organizations/{organizationId}/notifications/{notificationId}/read` marks
one notification read. `POST /organizations/{organizationId}/notifications/read`
accepts `{ "ids": [ ... ] }` for one to 50 distinct, nonempty IDs. Bulk actions
apply to the explicitly selected set; new arrivals are not implicitly marked read.
One inaccessible, missing or foreign-recipient ID rejects the entire selection.
Single and bulk actions acquire the same sorted scopes and recheck visibility,
recipient and session before acknowledgment. The transaction rolls back read
changes if final session validation fails.

Read actions preserve the first read timestamp and return the canonical ID/read
time pairs. Optional existing Work retry keys retain the original acknowledgment;
replay still requires current access. Reordering the same bulk selection has the
same fingerprint. Reusing its key for a different selection is a conflict. A
retry never clears or advances a prior read timestamp. Read-state updates are
recipient preferences and remain permitted while an Organization is archived.

The API database role can update only `read_at`, in addition to its existing
SELECT/INSERT privileges; IDs, recipient, actor, scope and creation metadata remain
immutable through that role. Worker has no notification-table access. Migration
032 adds the recipient/newest-first index; API/Worker schema admission and upgrade
checks require all 32 migrations. No new service, datastore or queue is introduced.

Native telemetry uses fixed `notification_read`, `notification_mark_read` and
`notification_bulk_read` operations and bounded error codes. Metric labels contain
no notification/recipient/Organization/Card IDs, links, timestamps or request bodies.

Added verification covers real host binding/cookies, recipient selection denial,
minimal response fields, newest-first 50-plus-2 pagination, filtering archived Cards
before the limit, single/bulk/natural/keyed retry, changed selection, mixed missing
IDs, invalid selections, access removal/restoration and archived parents. The
required exact-image fixture adds read-update permission failure with original-key
retry, observed Board waits during recipient access/session removal, historical
receipt denial, 53 real assignment producer notifications with one archived Card,
50-plus-2 paging and bulk replay. Runtime-role and ordered migration checks include
the new column grant and index.

Local compilation and shell syntax checks passed; new Linux host and exact-image
runtime execution remain pending. The prior producer commit d0b3b80 passed Linux
host, PostgreSQL storage/migration/roles, web and image/security checks; its full
container runtime remains in progress. The MUI notification center, recipient
live recovery, watching, mentions, reminders, accessibility and performance
acceptance still require implementation/evidence. PRD-17 and PRD-11 remain open.

Subsequent authoritative job observation on run 37056535453 confirms the exact-image
Card assignment/notification producer/account cleanup fixture and the live
write/lifecycle scope fixture passed. This proves those scoped runtime checks,
including the previously repaired event count and archived-Organization admission;
the complete required-CI gate and the new inbox consumer checks remain pending.

## Internal notification center

The MUI center is available at `/app/{organizationId}/notifications` through the
desktop navigation and the header button on mobile. Each bounded page replaces
the preceding one. It shows assignment type, read/unread text, recipient-local
dates and the affected Card link. It supports single-row read and explicit bulk
selection of the current page; it does not claim a global unread count.

Every refresh reads `/me` before validating the recipient, Organization, safe
entity links and timestamp/UUID seek ordering. Sub-millisecond timestamp precision
is preserved. Read acknowledgments must match every original selected ID exactly.
An uncertain response retains the original recipient, selection and retry key,
even after a newer page is loaded. New writes are disabled until that intent is
confirmed or retired. Access loss, account change and Organization navigation
retire private data and fence late responses. The center refreshes every ten
seconds while visible and on window focus; profile events also invalidate it.
It does not claim notification-specific SignalR delivery.

Twenty-three validation/component scenarios cover precision, paging, safe links,
recipient scope, read acknowledgments, exact retry after response loss, account
switch, denied access, periodic HTTP recovery, rejected selections, keyboard focus
and late-response cancellation. A new
release browser scenario uses real producer assignments, desktop and 390px phone
views, a committed read with a lost acknowledgment, same-key recovery, selected
bulk read, automatic cross-client refresh and Board membership removal. Browser
collection passed; runtime execution is pending CI.

Commit ec6b142 repairs retry-key admission for both Organization inbox read
routes. Linux host tests, PostgreSQL checks, web quality, image builds and scans
passed on run 37059359898; its container gate remains pending. Watching,
mentions, reminders, required notification events and performance evidence still
remain before PRD-17/PRD-11 closure.

Subsequent run 37059359898 observation confirms the exact-image bounded Board
member/Card assignment/notification inbox fixture passed, including read-update
rollback, recipient admission after observed lock waits and bulk paging/replay.
The live database write/lifecycle fixture also passed. Identity mail and browser
checks were still running; this does not assert a completed required-CI gate.

Reconnect and accessibility continuation: the MUI inbox now re-admits its current bounded page on the browser online event, in addition to focus/visibility and the existing 10-second polling. Notification articles and selection controls use their actual configured event label, including watched activity and mentions. Component coverage verifies offline read failure hides content, online recovery requires fresh reads, and the listener is removed on unmount; 51 notification parser/component cases pass, followed by 17 component cases after added accessible-label assertions. Typecheck and lint pass. The native desktop/phone scenario now creates an assignment while the phone is offline, recovers it without manual navigation, marks it read across both views, and checks accessibility on both widths. Collection passes; actual immutable-image browser execution and full PRD-17 acceptance remain pending. These HTTP recovery checks do not establish recipient-private notification realtime events.

Inbox disclosure now validates the active account both before and after its bounded notification read. A changed recipient retires the page, selection and uncertain command intent; malformed or unavailable post-read admission withholds the entire page and private diagnostics. The latest admitted profile supplies date formatting. Three regressions hold the second account read and prove no Card link, notification article or mutation control is exposed before successful admission, then reject changed, malformed and unavailable responses. All 54 notification parser/component cases, typecheck and lint pass. Server authorization remains authoritative; actual native/full release acceptance is pending.

The inbox records content-free client open/use, read and mark-read outcome latency, retry/conflict/exception observations and online reconnect through the existing bounded ActivityClient transport. Fixed action/kind labels are documented in activity-history-telemetry.md. A privacy regression covers lost acknowledgment retry and offline/online recovery without leaking notification selections, links, accounts, scope, keys or diagnostics. Twenty-five focused component/transport tests, typecheck/lint and browser source typechecking pass; six server privacy parser cases compile under strict solution build, with runtime CI pending. These observations do not establish recipient-private notification realtime event delivery.
