# PRD-13 acceptance audit

The authoritative requirements are GitHub issue #14. This audit preserves the
full ticket scope; implementing command controls is not sufficient for closure.
It includes the source, interaction and measurement coverage added after item ordering.

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
| TC-05 permission changes | Host replays revalidate current rights; component checks hide/disable protected data and commands during re-admission; desktop/mobile collaboration cases cover dirty drafts and revoke contributor membership with a real committed/lost-reply item command unresolved, check UI recovery/content removal, denied reads/new writes/original-key replay and exact unchanged owner-visible canonical content | Execute the new real-image cases; browser evidence remains pending |
| TC-06/07 timeout/idempotency | Original actor/body/key/revision recovery in host/image/component tests | Execute real server-committed/lost-response browser fixtures for every command |
| TC-08 concurrent clients | Server Card/Checklist/item CAS and local dirty-draft/conflict checks; desktop/mobile collaboration cases now hold an actual submitted contributor request while the owner commits through UI, assert server 409 and retained dirty choices, then explicitly discard/review the canonical item | Execute the new real-image cases; no passing-browser claim from discovery |
| TC-09 disconnect recovery | Shared Board event delivery/replay infrastructure; content-free Card aggregate events; collaboration cases now close the actual proxied socket, reject reconnects, recover missed completion through HTTP events and observe another change after reconnect without page navigation | Execute the new real-image cases; authored/discovered cases are not passing evidence |
| TC-10 lifecycle | Host/image Card/List archive/restore/delete retains exact children, fences writes and prior receipts; List copy excludes tombstones; four new Card/List desktop/mobile browser cases hold a committed rename acknowledgment across archive/restore/delete, check read-only retention/replay fencing and restored original receipt, and remove deleted scope | Execute the new real-image cases; shared PRD-18 retention work remains separate unfinished scope |
| TC-11/12 keyboard/mobile | Accessible MUI names/status/progress, component focus tests; new 1280/390px browser cases authored | Execute full WCAG/keyboard cases; verify Board scroll/context preservation for Checklist interactions |
| TC-13 large data/performance | Bounded 50+1 seek pages and full 63-row aggregate/copy/cascade fixtures; shared normal Board performance evidence; new normal Checklist benchmark asserts 50/13 item pages with full progress and measures 50-Card Board readiness, cached detail, creation feedback and 20 actual completion mutations; new mandatory image capacity fixture seeds 200 Lists/5000 active Cards/100000 archived Cards and exercises actual API reads/seek pages/completion | Execute both fixtures against exact images and retain evidence; unchanged normal 1500/200/100/500 ms budgets; large client/scroll/context behavior still needs verification |

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

The Checklist benchmark holds creation before server submission and measures the
first rendered pending status/disabled submit control from the captured submit
event. This measures immediate feedback, not optimistic server success. It then
allows the actual request and checks its canonical revision. Twenty alternating
completion commands must each change Card/Checklist/item revisions; p95 includes
real HTTP and acknowledgment parsing. Item-page latency is recorded separately
without inventing a ticket budget. Retained evidence includes only fixed fixture
sizes, timings, sample arrays, budgets and executed status, never object IDs or
Checklist text. Seven reporter tests and ten creation-control tests pass locally;
the benchmark is discovered, not yet executed. Capacity and telemetry remain open.

`scripts/ci/test-checklist-capacity.sh` is a separate mandatory container stage
using the already-built release API/PostgreSQL/Nginx topology. It creates an
isolated authenticated owner/Organization/Board through API, then seeds canonical
disposable scale rows directly in PostgreSQL. Seeding is not audited-command
acceptance evidence. It asserts all 200 Lists and 5000 active Cards in the Board
snapshot, 100000 archived Cards in the Organization, bounded first/second/final
archive seek pages, 50/13 Checklist item pages, whole-Checklist progress,
unchanged protected/audit/event/receipt state after reads and one actual
versioned completion. Fixed-scope timings and results are retained for 90 days
with the exact commit; IDs, titles, keys and credentials are excluded. Shell
syntax passed locally. Docker is unavailable locally, so capacity execution and
measurements remain pending CI. This API fixture does not prove large client
rendering, normal UI latency, production capacity or telemetry acceptance.

Checklist client observations now cover disclosure, validated reads, mutation
outcomes/timings, explicit receipt retries and caught exception/conflict categories
through a bounded authenticated/CSRF-protected aggregate endpoint. See
`docs/kanban-telemetry.md` for protocol and remaining operator/realtime/render
coverage. This partial instrumentation does not close PRD-13. Latest source run
37105725659 at cefeef3 passed .NET/PostgreSQL but failed one Checklist rename
recovery callback assertion (864 web tests passed). The assertion now awaits the
existing React effect; its expected value and exact original-body/key checks are
unchanged. Image/capacity/security stages were skipped in that failed run.

Capacity run 37105533441 at 55da719 passed source, image-build and security gates,
but container job 111154138044 failed after the capacity fixture assertions at
JSON evidence generation: jq requires parentheses around arithmetic object
values. The uploaded evidence artifact was consequently empty and proves no
successful capacity stage. Commit 3962bbf repairs those expressions and generates
into scratch before publishing the final file. The exact expression now produces
valid JSON and expected millisecond conversion locally; shell syntax also passes.
The mandatory capacity/browser/release gates remain required and unproven.
Full local web regression after client instrumentation and callback repair: 869 passed across 66 files; zero failures. This is local web evidence, not Linux host/image acceptance.
