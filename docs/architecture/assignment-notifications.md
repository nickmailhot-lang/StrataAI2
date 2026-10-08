# Assignment notification persistence (PRD-11 / PRD-17)

An actual Card member addition creates one `CARD_ASSIGNED` notification for the
new assignee, unless that assignee is the actor. The Card association and revision,
audit, content-free Work event, existing Worker delivery job, notification and
retry acknowledgment use the originating command's single PostgreSQL transaction.
Failure to persist the notification rolls back the entire assignment. No second
connection or external message delivery is introduced.

Migration 031 adds `card_assignment_notifications`, with forced tenant RLS,
composite Card/Board and Work event/Board foreign keys, Organization-scoped
recipient and actor references, a positive Card version, timestamps and nullable
read timestamp. Notification type is `CARD_ASSIGNED`. Event-recipient uniqueness
retains the original notification identity. Membership departure does not delete
notification history; future inbox admission must hide inaccessible records.
Self-notifications are rejected by a database constraint and suppressed by the
application before insertion. API receives SELECT/INSERT only. Worker has no
table access; its existing Work event readiness envelope remains unchanged.

The producer validates that the originating event has the same Board, Card,
actor, version and creation time and is a `CARD_MEMBER_ADDED` Card event. Assigning
an already assigned person, a stale/conflicting request, completed receipt replay
and unassignment create no additional notification. A later genuine reassignment
has a new event and produces a new notification. Removal by Board/Organization
departure or account deactivation leaves prior notifications intact.

No recipient data is added to the public/Board Work replay envelope, whose
metadata remains empty. Notifications store IDs, type, revision and timestamps;
Card titles, descriptions, emails, delivery secrets and message bodies are absent.
The internal store's bounded 51-row window is **not an authorized inbox**. The
subsequently implemented [authorized inbox](notification-inbox.md) separately
enforces its HTTP/browser admission. Inbox service work must freshly authorize the
current recipient, Organization membership, Board/entity visibility and session,
including after lock waits, before disclosure or marking read.

Required checks added with this slice:

- Host assignment tests inspect recipient/actor/Card/version, self suppression,
  exact receipt replay, no-op assignment and retained history after unassignment.
- Demo store tests check event-recipient deduplication, changed-intent rejection,
  self suppression, bounded 50-plus-sentinel seeking and tenant/recipient isolation.
- PostgreSQL storage checks cover forced RLS, tenant reads/writes, composite
  references, event-recipient uniqueness, self suppression, version/read-time
  constraints and retained history. Runtime-role checks require API SELECT/INSERT
  only and no Worker privileges.
- The exact release-image assignment fixture fails notification INSERT after
  association/revision/audit/event/job writes, checks all protected state is
  unchanged, and retries the original key. It also checks one exact event-linked
  notification, self suppression, unchanged no-op/replay state and reassignment.
- Migration upgrade/repeat/concurrent-run/failure rollback checks now include 031;
  API and Worker schema admission require all 31 migrations.

The local full .NET build passed with warnings treated as errors and shell syntax
checks passed. Host/domain test execution, PostgreSQL storage and exact-image
runtime checks await Linux CI. This is the assignment producer slice: notification
center, individual/bulk read actions, recipient-scoped live recovery, watching,
mentions, due reminders, accessibility and performance acceptance remain required.
PRD-11 and PRD-17 are not ready for closure.

CI run 37053967758 reached the account-deactivation assignment cleanup checks:
rollback, physical removals, affected Card revisions, preserved other assignees
and account/membership state passed. Its event-count assertion counted an earlier
Organization leave event from the same actor. The fixture now requires an increase
of exactly three events plus exactly one event per resulting Card version. Its
remaining replay assertions must still execute successfully in a subsequent run.

## Current native producer-to-recipient evidence

The [desktop/phone assignment recovery cases](../card-assignment.md#executed-native-assignment-producer-and-private-inbox)
pass actual MUI assignment, original-key acknowledgment recovery, unassignment
history retention, genuine reassignment, actor self-suppression, private live
identity/journal matching and membership withdrawal. Both native recipient
inboxes show canonical Card links and typed persisted attribution. This extends
the historical producer slice above into an actual native consumer workflow.
It is local compiled evidence with optional verification, not current retained
release images or full producer/role/concurrency/capacity acceptance. PRD-11
remains open at **35% estimated work remaining**, and PRD-17 at **21%**.

## Complete persisted native attribution

The [final desktop/phone producer-family checks](prd-17-acceptance.md#executed-complete-attribution-across-notification-producer-families)
compare both actual assignment notifications with their Card source revisions,
actors, scope, type and clocks. Complete private creation envelopes match stored
journal identities, versions, decimal sequences and timestamps. Original receipt,
reassignment, self-suppression, accessibility and membership-withdrawal assertions
pass unchanged. This is local compiled evidence with optional API verification;
current retained-image and full acceptance remain required. PRD-11 remains open
at **35% estimated work remaining**; PRD-17 remains **15%**.

## Strict verified-account native assignment delivery

The [eight-case strict producer invocation](prd-17-acceptance.md#executed-strict-verified-account-notification-producer-families)
passes both assignment/reassignment widths with API and Worker verification
required. Each fresh author/recipient login is refused before verification and
accepted afterward. Native recovery, complete persisted attribution/envelopes,
self-suppression and current withdrawal remain unchanged. A required immutable-image
CI phase now runs these cases before optional profile fixtures. Local compiled
evidence does not establish email-provider/full release acceptance. PRD-11 stays
open at **35% estimated work remaining**; PRD-17 **15%**.
