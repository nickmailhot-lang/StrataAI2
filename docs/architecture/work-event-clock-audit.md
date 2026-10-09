# Work event delivery clock audit

[PRD-01 FOUND-FR-009](prd-01-acceptance.md) still requires an update-clock
repair for mutable Work event delivery state. The Organization event clock
repair in [migration 121](organization-event-clocks.md) cannot be copied here
without addressing different history and delivery rules.

## Recorded behavior

Migration 011 gives `work_events` an immutable event `created_at` and nullable
mutable `ready_at`, but no `updated_at`. The separate `work_event_streams`
counter already has both physical audit timestamps. Its absence is not a gap.

Migration 062's `enforce_activity_event_immutable` compares the entire event
row as JSON, excluding only `ready_at`. Migration 063 runs that comparison
after updates so stored generated target references participate in the check.
Adding an automatically changed clock without adapting this exact comparison
would make successful publication fail. Excluding a managed clock from the
payload comparison must retain separate protection against clock tampering;
immutable identity, attribution, payload and generated references must remain
protected.

The deployed role provisioning grants the Worker `UPDATE(ready_at)` and a
limited set of readable identity/readiness columns. It does not grant payload
updates or whole-table writes. `PostgresWorkEventDeliveryStore` locks a matching
unexpired `WORK_EVENT_READY` claim, changes only a pending source, and verifies
the lease again before committing. Duplicate store delivery preserves readiness.
Those application checks do not make `ready_at` intrinsically immutable in SQL.
The existing history guard permits replacing or clearing that field.

The original `ActivityEventSourceStoreContract` explicitly publishes one
source, checks journal/discovery readers, then clears its readiness again to
test the pending barrier. That reset is an administrative fixture operation,
not a demonstrated production Worker reset. Nonetheless it proves that the
schema supports more transitions than pending-to-first-publication.

Consequently `COALESCE(ready_at, created_at)` would return the original creation
time after a reset and would lose the time of the last mutation. A legacy
null readiness value alone cannot distinguish never-published from reset
history. Historical mutation time must not be invented from migration time,
current wall time or lease expiry. The eventual repair needs both justified
legacy provenance and clock maintenance for every admitted delivery-state
transition, while preserving existing journal/readiness behavior and claim
fences. This gap remains in the acceptance audit.

## Executed local verification

The unmodified original activity source contract passes against schema 121
with real PostgreSQL/pgvector, a restricted API login and the complete API/Worker
required-ledger readiness contract. This includes the publish/reset pending
barrier, original event identities, bounded 65-event same-time history window
and cursor/permission checks. Accounts and trusted application context are
synthetic; the reset is performed by the fixture administrator. This is not
browser transport, production Worker-reset authority or immutable-image release
acceptance. Reports are retained privately in
`work-event-clock-audit-native-20261009`.

The next complete Board browser phase runs the repaired frontend with compiled
schema-121 API/Worker binaries mounted read-only into local runtime containers.
Collection verifies all fourteen original files and 32 cases at desktop,
tablet and mobile widths. Its report is pending; there are no narrowed tests
or test retries. PRD-01 remains open at **34% estimated work remaining**
(planning estimate).
