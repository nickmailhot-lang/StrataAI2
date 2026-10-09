# Organization event delivery clocks

[PRD-01 FOUND-FR-009](prd-01-acceptance.md) requires audit timestamps on
mutable records. Organization metadata and terminal lifecycle journals retain
immutable event payloads, but first publication changes their `ready_at` state.
Migration 121 adds a stored generated `updated_at` to both journals:
`COALESCE(ready_at, created_at)`.

Pending events retain the actual event creation timestamp. Delivered events
retain the recorded first publication timestamp. The upgrade uses neither
migration time nor current Organization state. Generated values cannot be
supplied or replaced by callers. Existing payload and first-publication history
guards remain, as do tenant RLS, runtime grants and leased Worker capabilities.
Finite-time/order constraints reject invalid historical clocks atomically.
API and Worker startup require ledger `121_organization_event_delivery_clocks`.

Metadata history is protected by migration 095's explicit immutable payload
comparison and prohibition on rewriting a non-null `ready_at`. Its delivery
capability, including later replacements, records
`GREATEST(clock_timestamp(), created_at)` only while publication is pending.
Lifecycle history uses the corresponding guard in migration 089 and delivery
capability in migration 090. Both capabilities validate the current lease
before and after delivery; an expired late fence rolls back publication and
the derived update clock together. Replay preserves the first publication.

## Verification scope

The complete migration gate adds populated pending/delivered metadata events,
compares every historical field before/after upgrade, verifies the derived
clock, executes a first publication, and rejects generated-clock replacement,
publication replacement, payload tampering and deletion. No-op/rejected writes
must retain the clock. Original clean/repeat/forward/refusal/serialization and
failed/unrecorded migration checks remain; fixture numbers advance to 122–124.

Lifecycle publication additionally requires the actual leased terminal
transition and its same-transaction invitation authority proof. An initial
schema fixture attempted direct lifecycle insertion and was correctly refused;
that failed run is retained privately. The fixture was corrected without
changing or disabling the guard. Separate local verification uses the existing
restricted Worker terminal contract on schema 120, compares its populated
history through migration 121, then runs the current metadata and terminal
contracts. Their existing bad-scope/actor/lease/worker checks, late-expiry
rollback, duplicate/reclaimed delivery and disabled direct writes now also
check stable creation and derived update clocks at every readiness observation.

The complete local migration gate and the populated lifecycle upgrade/current
restricted delivery/readiness contracts pass. The locked Release build passes
with zero warnings or errors. Independent cleanup and source checks accompany
the private retained reports. These are executed local results, with the release
limitations below still outstanding.

These contracts stage accounts, earlier graph deletion and trusted application
context. They do not establish browser/HTTP authentication, full graph traversal,
external providers or current immutable-image CI. The overall mutable-record
audit, including other journal counters and Work event delivery state, remains
open. No PRD closure follows from this bounded repair.

The [Work event clock audit](work-event-clock-audit.md) records its distinct
full-row history guard, permitted readiness reset and missing legacy mutation
provenance. Its original activity/reset contract passes on schema 121; this
does not resolve the missing update clock.
