# PRD-13 acceptance audit

The authoritative requirements are GitHub issue #14. This audit preserves the
full ticket scope; implementing command controls is not sufficient for closure.
It was refreshed after item ordering commit `160afed`.

## Functional requirements

| Requirement | Current implementation | Evidence and remaining verification |
| --- | --- | --- |
| CHECK-FR-001/002 | Multiple persisted ordered Checklists, stable scoped identity/title/rank/time/version | Domain/API tests, forced-RLS migration 040 and exact-image Checklist fixture; full new UI browser execution pending |
| CHECK-FR-003 | Ordered scoped items with completion actor/time | `ChecklistTests.cs`, `ChecklistApiTests.cs`, strict browser response parsers; completion/history UI browser cases pending |
| CHECK-FR-004 | Checklist create/rename/position/admin-confirmed cascade delete | Application commands and MUI create/root manager; command host/image fixtures and local component tests; full browser suite pending |
| CHECK-FR-005 | Item create/edit/complete/uncomplete/position/admin-confirmed delete | Application commands and item manager; host/image fixtures and local component tests; full browser suite pending |
| CHECK-FR-006/007 | Progress uses all active items independent of the current page; empty is 0% | API aggregate and parser tests, 63-item exact-image fixture; UI two-session/empty-progress browser cases pending |
| CHECK-FR-008 | No item member assignment or due fields | Domain/schema/API shapes and item editors; item completion remains independent of Card due completion |

## Interaction and scenario coverage

| Scenario | Current evidence | What is still required |
| --- | --- | --- |
| TC-01/02 primary/empty | Source/host/image commands; local reader/create/root/item manager tests | Execute all desktop/mobile Checklist browser cases against the exact release images |
| TC-03/04 input/authorization | Domain validation, host permission/tenant/public-read cases, image RLS and post-wait visibility checks | Confirm current exact-image release gate; preserve disclosure-before-validation fencing |
| TC-05 permission changes | Host replays revalidate current rights; component checks hide/disable protected data and commands during re-admission | Dedicated browser interaction with rights revoked while a Checklist/item draft or recovery is open |
| TC-06/07 timeout/idempotency | Original actor/body/key/revision recovery in host/image/component tests | Execute real server-committed/lost-response browser fixtures for every command |
| TC-08 concurrent clients | Server Card/Checklist/item CAS and local dirty-draft/conflict checks | Dedicated browser concurrent draft/update/conflict reconciliation scenario |
| TC-09 disconnect recovery | Shared Board event delivery/replay infrastructure; content-free Card aggregate events | Dedicated Checklist browser socket loss/missed-event/reconnect recovery scenario |
| TC-10 lifecycle | Host/image Card/List archive/restore/delete retains exact children, fences writes and prior receipts; List copy excludes tombstones | Verify live browser lifecycle/access transition during Checklist interaction; shared PRD-18 retention work remains separate unfinished scope |
| TC-11/12 keyboard/mobile | Accessible MUI names/status/progress, component focus tests; new 1280/390px browser cases authored | Execute full WCAG/keyboard cases; verify Board scroll/context preservation for Checklist interactions |
| TC-13 large data/performance | Bounded 50+1 seek pages and full 63-row aggregate/copy/cascade fixtures; shared normal Board performance evidence | Checklist-specific feedback/mutation/cached-detail measurements and documented 200-List/5000-Card/100000-archived-Card capacity evidence |

## Events, telemetry and shared dependencies

Application commands emit the required Checklist/item event types through the
existing transactional outbox and per-child append-only audit. Card aggregate
versions invalidate authorized Board readers; raw Checklist text is not in work
events. Existing image fixtures cover late audit/event/queue failures and
transactional rollback. These are source/host/image facts, not proof that all new
client interactions satisfy realtime acceptance.

`BoardSharingTelemetry` maps all Checklist read/command routes to fixed operator
request counters and duration histograms, with bounded outcome/error-code/keyed-
attempt labels. It excludes tenant/object/actor IDs, content, raw URLs and keys.
HTTP reads and keyed attempts do not prove client feature opens or user-visible
retries. Client open/use/exception/retry reporting, reconnect/conflict rates and
operator collection/export evidence remain incomplete, as described by the
shared telemetry documents. Audit remains authoritative for business history.

The ticket also depends on PRD-08/22 and cross-PRD lifecycle/copy behavior. New
source stages are passing, but exact-image browser runs are live or pending;
known earlier label/assignee keyboard failures have a repair on main pending
execution. No full release-green or PRD-13 closure is claimed. Future updates must
replace pending entries with authoritative executed evidence, not infer success
from compilation, test discovery or a narrow passing stage.
