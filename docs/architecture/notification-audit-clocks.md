# Notification audit clocks — PRD-01/17

FOUND-FR-009 and the notification data requirements require creation and update
audit clocks on mutable notifications. The canonical `CardNotification` and
authorized inbox item now expose `updatedAt`. Its value is the persisted first
`readAt` when read, otherwise the originating `createdAt`. The historical source
envelope stays bound to its original event; current Board location is a read
projection. The clock describes notification read state, not later Card activity.

Both facts are already durable. The restricted adapter persists the first read
with `COALESCE(read_at, GREATEST(created_at, accepted_time))`; the private journal
rejects changes to an existing first-read time. The canonical property derives
the clock from these facts, including the database's actual timestamp precision.
It has no independently writable backing value. Future mutable notification
states must extend this clock rule before they are introduced.

The inbox response adds `updatedAt`; the recipient-bound read acknowledgment
continues to report the original `readAt`. Internal notification serialization
also exposes the audit clock. Existing recipient/entity authorization precedes
inbox disclosure. Existing client parsers consume the fields needed for display;
date preferences do not rewrite the notification's stored clock.

The existing API-host recipient/bulk-read regression first fails on the missing
clock. After repair, all 17 selected notification API cases pass. The regression
requires creation clock equality, first-read clock retention through original-key
and natural retries, unchanged unread clock after refused mixed selections, and
first-read preservation across bulk reads and conflicting key reuse. The locked
Release solution build passes with zero warnings/errors.

The mandatory [restricted mention contract](../../tests/StrataAI.Persistence.Contracts/CommentMentionNotificationContract.cs)
also requires unread clock equality, authoritative stored first-read precision,
unchanged metadata after later read/source replay, and owning transaction rollback
of Card/comment/snapshot/event/notification/delivery effects. Its focused argument
is `--comment-mention-notifications-only`; the ordinary contract suite still runs
it. A fresh PostgreSQL 17/pgvector schema-114 invocation passes this contract and
the API/Worker required-ledger refusal/restoration check. The reused harness also
verifies the actual schema-113-to-114 historical identity-clock upgrade. Fixture
accounts/source setup and in-process transaction admission are synthetic; this
does not prove HTTP session authorization, mail or deployed Worker transport.
The owned proof container is removed and original services/data remain intact.

Current immutable-image/full release gates and complete PRD requirements remain
outstanding. Estimated work remaining stays **34% for PRD-01** and **15% for
PRD-17** (planning estimates); neither issue is ready for closure.
