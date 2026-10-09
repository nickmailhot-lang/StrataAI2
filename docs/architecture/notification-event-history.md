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

## Subsequent native focus results

The schema-127 nine-case invocation subsequently finishes **8/9** in 617.54
seconds, with one result per case and zero skipped/flaky outcomes. The phone
confirmed-group-mention scenario fails its automatic original-comment retry
focus assertion at `comment-mass-mentions.spec.ts:119`. Its source publication,
history guard or complete native acceptance is not inferred from eight passing
cases. The final whole-counter oracle does not execute after browser failure.
Failed trace/report are retained; owned containers, database and credentials
are independently absent.

Source review finds a related comment-recovery gap: the component captures its
dialog when an action begins but omits that dialog from subsequent ownership
checks after the original action is removed. The shared focus helper already
supports that captured fallback. Both blur and restoration checks now pass it,
retaining the rule that another control or another dialog owns its own focus.

Two new isolated dialog-fallback regressions fail before the change at the
post-removal restoration assertion and pass afterward. They model both same-root
sentinels with the actual removed action and connected dialog. An initial
real-Dialog fixture stops at its sentinel-focus setup because MUI immediately
redirects sentinel focus; that setup result is retained and is not the product
regression. The isolated fixture preserves the existing real-Dialog tests.
All **37** comment/shared-focus cases pass without skips, including original
key/body preservation and respect for another focus owner. Web types, targeted
lint and the production frontend build pass; output and reports remain private.

These regressions prove the captured-dialog gap, not that it was the unique
cause of the phone native failure. A fresh complete original nine-case phase
now uses the repaired MUI assets, strict schema-127 API and separate Worker,
retaining every original producer/consumer assertion and successful-exit
whole-counter oracle. The full watch/order phase is separately running with
enabled/focused native watch-retry observation. Their terminal results, original
capacity acceptance, complete current-source verification and immutable
build-once CI remain outstanding. No ticket is closed.

The captured-dialog-fixed nine-case invocation subsequently finishes **8/9**
in 633.97 seconds, with exactly one result per original case and zero
skipped/flaky outcomes or report-level errors. The desktop confirmed-group case
passes its earlier automatic retry focus assertion, then fails a later
`Comment added.` visibility assertion at `comment-mass-mentions.spec.ts:141`
while publishing subsequent confirmed mentions. That later failure remains
unresolved; a complete producer/consumer pass is not inferred. The final
whole-counter oracle does not execute after browser failure. Owned containers,
database and environment files are independently absent; private diagnostics
are retained for the next investigation.

The separate complete schema-127 watch/order phase now passes **12/12** and
its whole-counter source-clock oracle, as recorded in the
[clock verification record](notification-stream-clocks.md). This does not
replace the incomplete nine-case phase, capacity acceptance, current full-source
verification or immutable build-once release proof. PRD-17 remains open at
**15% estimated work remaining** (planning estimate).

## Subsequent comment-source admission observation

The retained nine-case report identifies the later failure at **1280px**;
the phone case passes in that invocation. Earlier issue commentary described
the later failure as phone and is corrected by the authoritative report.
Read-only trace inspection finds the desktop Save action followed by its
pre-save account check, no subsequent comment POST, and the existing local
unavailable-change announcement. The overlapping protected Board read retains
Card revision 4 and edit permission. This is not proof of a server publication
failure or a reason to bypass the foreground authorization boundary.

The native fixture previously waited for the Worker drain and an observed Card
revision, which could both precede delivery of the corresponding Board source
frame. Before each subsequent draft it now additionally requires the actual
`COMMENT_ADDED` sources and the protected Board read started after that source
epoch, using the existing passive history tracker. In this fixture assignment
establishes revision 2 and each following revision is one admitted comment;
the required source count is the current revision minus 2. Replayed frames do
not count twice, and a read from an older epoch cannot satisfy admission.

Browser typecheck and all **15** tracker tests pass, with zero failures,
cancelled or skipped cases. The production component and its frozen assets are
unchanged by this observation repair. A fresh complete original nine-case
invocation retains all original source/privacy, key/body, quota, role-scope,
keyboard, mobile and lifecycle assertions, deadlines and zero test retries.
Its actual terminal result and successful-exit whole-counter oracle remain
required; this change is not itself native acceptance. PRD-15 and PRD-17 remain
open at **34%** and **15% estimated work remaining**, respectively.
