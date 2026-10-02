# Authorized assignment inbox (PRD-17)

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
