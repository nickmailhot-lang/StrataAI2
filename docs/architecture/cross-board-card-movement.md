# Cross-Board Card movement: implementation dependencies

PRD-08 requires destination permissions and Board-scoped reference integrity;
PRD-15 requires readable historical activity after actual movement. The current
transactional decorator, application command and both stores still reject
cross-Board movement. This document tracks the full implementation dependency,
not an alternative definition of completion.

## Historical notification storage

Migration `066_notification_historical_card` separates the immutable source Board
from the stable Card's current Board. Notifications keep their original Board,
event, actor, recipient, Card version and timestamp. They still reference the
same-tenant Card and the original source event/type. An additional typed source
constraint binds a Card event's entity ID to the notification's Card ID, or a
Reminder event through its immutable Reminder-to-Card reference. An unrelated
same-tenant Card cannot replace the historical subject. Valid existing rows are
checked during upgrade; an AFTER trigger validates inserts and changed source
identities once generated event-type fields are available. Read-at changes retain
the admitted source identity.
Current API/Worker grants and forced RLS remain unchanged.

The populated migration runner inserts a valid historical notification before
the upgrade and compares its complete JSON envelope after two migration runs.
It first injects a source mismatch accepted by the former same-Board FK and
requires upgrade refusal with all DDL and ledger changes rolled back; only the
disposable fixture is then repaired before verifying the successful upgrade.
The restricted storage contract then moves a real database Card to another
same-tenant Board after clearing its fixture's current assignments, proves all
notification envelopes unchanged, rejects same-tenant subject rebinding, retains
tenant isolation, and permits read-at acknowledgement without changing source
attribution. This database fixture does not prove the authorized move API or
assignment migration policy. Linux CI must execute these new SQL cases.

The runtime schema requirement and exact-image missing-migration/refusal/restore
fixture require all 66 real migrations. The runner's synthetic serialization,
failure and unrecorded fixtures are numbered 067–069.

## Remaining command and consumer work

- Acquire source and destination Board gates in canonical order, with fresh
  session, Organization, both Board permissions and active parents after waits.
- Preserve current within-Board command fingerprints; cross-Board exact retry
  must admit the original source and current destination safely after movement.
- Move current Board-scoped labels/assignments according to explicitly recorded
  cross-feature rules, without corrupting or deleting historical notifications.
- Update rank/Board/List and routing atomically, with one Card revision and
  consistent source/destination activity, audit, realtime and durable receipts.
- Reconcile Watch, Reminder, Checklist, Attachment, cover and comment current
  authorization and scheduling through their stable Card relationship.
- Update notification admission/projection: the existing inbox deliberately
  joins the Card's current Board to the historical source Board, so moved
  notifications remain hidden until an explicit current-authorized projection
  is implemented. Historical storage changes do not widen current disclosure.
- Add explicit MUI destination selection and concurrent/retry/revocation tests,
  actual two-client movement/reconnect and native keyboard/mobile acceptance.
- Verify supported capacity, visual feedback and server acknowledgement budgets
  through the adopted build-once release gate before closing PRD-08/15.

The prior archived-detail revision `8b9623e` passed .NET/API, web, PostgreSQL and
source-quality checks in run 37192240480. Immutable image/release completion was
still running when this dependency work began. No issue is closed by this stage.
