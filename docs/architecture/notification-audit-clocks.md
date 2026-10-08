# Watch and notification audit clocks — PRD-01/17

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

## Native watch activity and first-read acceptance

The notification group in [run 37848406366](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37848406366)
passed identity-mail bootstrap and both native recovery/verification viewports,
then failed the watch activity matrix's old full-row read comparison. All 26
changed diff lines were `updatedAt`: the assertion reset `readAt` alone while
expecting the unread creation clock to survive the first read.

The [watch activity scenario](../../tests/browser/watch-activity-matrix.spec.ts)
now requires unread `updatedAt === createdAt` and compares all thirteen complete
read rows against their original rows with exactly `readAt` and `updatedAt`
changed to the shared first-read timestamp. It independently checks each read
clock against persisted PostgreSQL `read_at` and preserves the original stored
creation time. Original-key and natural retries still preserve the entire
notification, Card and journal history; no fields are discarded from comparison.

Browser TypeScript and the complete scenario pass locally with the current
compiled Production API and separate Worker, frozen MUI assets, and restricted
PostgreSQL 17/pgvector schema 114. Verified-email enforcement stays enabled.
Only the newly registered disposable accounts are activated by the existing
fixture when mail tokens are private; provider delivery is separate acceptance.
Both desktop and phone clients use native controls and real private delivery,
including accessibility checks and recovery after a committed bulk-read response
is deliberately lost. The invocation's containers and cloned database are removed.
This is a scoped native proof, not current immutable-image/full-release success.

## Shared producer persistence oracle

The [independent browser persistence oracle](../../tests/browser/persistedNotificationDelivery.ts)
now also requires each authorized inbox `updatedAt` to equal the actual stored
first `read_at`, or original `created_at` while unread, preserving PostgreSQL's
timestamp precision. Its existing source/journal/recipient comparisons remain.
This extends FOUND-FR-009 verification across assignment, selected and group
mentions, and actual Worker due reminders rather than relying on one producer.

All eight strict native producer cases pass in one 8.2-minute invocation with no
skips or retries, using rebuilt current MUI assets, current compiled Production
API/separate Worker and restricted schema-114 PostgreSQL 17/pgvector. Both desktop
and phone views retain actual delivery, keyboard, accessibility, original receipt,
withdrawal and stored envelope checks. The
[comment confirmation repair and retained earlier failures](prd-15-acceptance.md#command-confirmation-through-automatic-recovery)
record the investigation and its limits. This local runtime proof does not certify
current immutable release images or mail-provider delivery. Owned fixtures are
removed and original services/data preserved.

## Watch clocks and observed permission ordering

The existing [actual watch/unwatch ordering scenario](../../tests/browser/watch-trigger-order.spec.ts)
now independently checks each persisted Card, List and Board subscription's
identity, owner, target, state, version, `createdAt` and `updatedAt`. Eighteen
read-only storage comparisons cover first watch, rewatch, both unwatch orders and
original source/watch retries. Creation identity/time remain stable through the
version-one-to-four sequence. Complete protected-history comparisons and all six
observed watch/activity races remain. Browser TypeScript and the expanded scenario
pass with strict verified-account Production HTTP and restricted schema-114
PostgreSQL 17/pgvector.

The complete five-case ordering phase also passes in one 7.9-minute invocation,
with no skips or retries. It exercises thirty observed request pairs: six watch
versus activity, eight permission withdrawal versus activity, and sixteen
single/bulk read versus withdrawal. Member/Admin grant removal and
Organization/Public visibility withdrawal are exercised in both orders. The
shared notification oracle now checks authoritative update clocks after actual
re-admission and successful reads. Denied reads and original retries preserve the
full retained graph, original clocks and currently authorized disclosure rules.

These checks use real Production sessions, HTTP commands and observed PostgreSQL
waiters/peer blockers. Fixture verification activates only freshly registered
disposable accounts when their mail tokens are private. No notification, read
clock or journal event is fabricated. No Worker is required for these originating
transaction checks; they do not add browser-input, private transport, mail-provider
or capacity acceptance. The required strict immutable-image phase retains both
ordering files. Owned containers/database are removed and original services/data
preserved; current full release gates remain separate.

Current immutable-image/full release gates and complete PRD requirements remain
outstanding. Estimated work remaining stays **34% for PRD-01** and **15% for
PRD-17** (planning estimates); neither issue is ready for closure.
