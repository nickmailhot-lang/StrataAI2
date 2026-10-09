# Retained notification journal integrity

This implements a writer-audit finding for [PRD-01](prd-01-acceptance.md) and
[PRD-17](prd-17-acceptance.md). `notification_events` stores immutable creation
and first-read facts; its creation clock comes from the owning notification
transition. A physical `updated_at` is not a missing mutable-entity clock when
the entire fact is immutable. Runtime API/Worker journal writes were already
denied, but source inspection found no database UPDATE/DELETE history guard.
Changing an event directly did not run the counter's clock-publication trigger.

[Migration 127](../../db/migrations/127_notification_event_history.sql) first
fences parent notification publication, counters and journal writes. It validates
every retained event against its notification identity, recipient, original
Board, actor and exact creation/first-read timestamp. Every counter must retain
complete positive sequence history, sequence-one creation and greatest event
time. Contradictory retained history aborts the entire migration without a ledger
entry or installed guard; no event, notification, counter or clock is repaired
by inventing a new source fact.

The new invoker trigger rejects all journal UPDATE/DELETE operations with
`23514`, including no-op updates. Inserts continue through the existing narrow
parent-trigger capabilities. No runtime role receives new table or function
permissions; PUBLIC execution is revoked and the function search path is fixed.
The restricted connection factories require the new ledger entry.

## Executed local proof

The [complete migration gate](../../scripts/ci/test-migration-runner.sh) passes
with its original clean/repeat/forward/refusal/serialization/failure checks and
these additions:

- A populated pre-upgrade transaction demonstrates that an event clock edit
  and deletion execute, then rolls both operations back.
- A deliberately contradictory retained clock causes upgrade refusal. The
  entire notification/counter/journal fingerprint stays unchanged, and neither
  the new trigger nor ledger entry appears. Restoring the exact retained source
  clock permits upgrade and repeat.
- Every retained event remains exactly equal after upgrade. Clock/identity
  edits, no-op updates and deletion are refused without changing history.
- The complete original source-admission and first-read producer fixture runs
  again after migration 127, preserving creation/read facts, exact counter
  clocks, replay, invalid-producer refusal and owning transaction rollback.

The full locked solution build passes with zero warnings/errors. All **234**
Node source checks pass without skips. The complete original tenant-schema,
RLS, runtime-role and Reminder SQL gates pass against schema 127; independent
readiness checks reject and restore all **127** migrated ledger entries for
both restricted factories. The new private guard function remains inaccessible
to both runtime roles. Staged sources match the repository, and both gates'
owned containers and credential files are independently absent.

These local results use fresh disposable PostgreSQL 17/pgvector fixtures and
compiled runtime artifacts, not the current immutable release images. A fresh
complete nine-case native notification producer/consumer phase is running on
schema 127 with strict verified accounts, separate real Worker and the original
private-delivery/persistence assertions. Its successful exit additionally
requires a whole-counter source-clock check. Its terminal result is pending.
The earlier schema-126 watch/order, capacity and full API runs retain their own
build/schema scope and live handles. Full current-source and build-once release
acceptance still govern closure. PRD-01 and PRD-17 remain open at **34%** and
**15% estimated work remaining**, respectively (planning estimates).
