# Notification counter audit clocks

This repair addresses a mutable counter gap in [foundation acceptance](prd-01-acceptance.md).
The private `(tenant_id, recipient_id)` counter in migration 068 allocates a
sequence before the owning notification-created or first-read journal insert.
Its existing advisory transaction lock, trigger capabilities, parent source
checks and reader permissions remain unchanged.

[Migration 126](../../db/migrations/126_notification_stream_clocks.sql) derives
creation from sequence one and update time from the greatest retained event
time. It refuses missing/gapped history, orphan journal scopes and nonfinite
source times before adding columns or a ledger entry. Positive unique sequence
constraints plus count/max equality prove upgrade continuity. Existing counter,
journal, notification body and immutable first-read facts are preserved.

Existing allocators retain their original counter-before-journal ordering.
Provisional insert defaults are replaced by the owning journal trigger in the
same transaction. A deferred guard checks the final counter and retained source
clocks. Source lookups use the sequence primary key and a scoped descending-time
index; no full-history count is added to the per-row runtime guard. Both new
private functions use fixed search paths and revoke public execution. No
runtime write or function-execution grant, policy, API envelope, event identity,
producer source or Worker lease changes.

The complete clean/repeat/forward migration gate passes with all original
assertions and four new whole-upgrade refusals: counter mismatch, a removed
event, orphan event scope and nonfinite event time. Each refusal independently
checks the full retained graph fingerprint, absent clock columns and absent
ledger entry. After restoring exact fixture history, repeated upgrade verifies
unchanged bodies and exact PostgreSQL source clocks. Creation/update tampering,
counter changes without source, infinity and no-op behavior are checked.

The unchanged complete source-admission fixture runs again after 126. Its late
invalid-row rollback, valid batches, original replay and Reminder source checks
remain. Additional real restricted parent first-read transitions and no-op
replay verify clock equality and immutable read facts before the whole fixture
rolls back. The pre/post graph fingerprint proves rollback. These tests do not
fabricate journal events or replace source times with the migration clock.

Independent checks match all five exercised source files and confirm zero owned
gate containers or credential files. The current locked solution build passes
with zero warnings/errors. Browser TypeScript passes; all 234 Node source cases
pass without skips. The Windows Python run has 51 passes and the existing
symbolic-link capability skip out of 52 cases.

The original complete nine-case strict notification browser phase now passes
against clean schema 126 with verified-account Production API, separate Worker,
restricted PostgreSQL 17/pgvector and the current compiled services. It covers
assignment, selected/group mentions, actual Worker reminders and inbox recovery.
The existing read-only persistence oracle additionally compares counter creation,
update, finite ordering and sequence/count equality directly in PostgreSQL,
retaining timestamp precision. All nine actual case rows have one successful
result, with zero skips, retries, flaky outcomes or report errors, in 632.93
seconds. All original cases, producer identities, receipts, realtime envelopes,
keyboard/accessibility assertions and deadlines remain. Independent checks
confirm zero owned browser containers, database or environment files and all
three original services running.

Full required-ledger readiness and the complete original tenant-schema, RLS,
runtime-role and Reminder-delivery SQL gates subsequently pass together in a
fresh isolated schema-126 invocation. The original readiness contract tests
its 48 declared removals, including 126, for both runtime roles; the role gate also
rejects direct execution of both new private functions. Original capacity measurements,
current immutable-image CI and complete notification/foundation acceptance
remain required. The separate schema-125 full API run retains its own build and
scope. No issue is ready for closure: PRD-01 remains at **34%** and PRD-17 at
**15% estimated work remaining** (planning estimates).

Source-to-contract comparison then identifies a coverage gap: startup requires
126 ledger versions, while that original readiness contract selects only 48.
The contract now enumerates the independently applied database ledger and
removes/restores every entry in turn for both restricted factories. Its original
complete-ledger admission, refusal, restoration and recovery assertions remain.
A separate build and fresh full readiness/security invocation now verify all
**126** ledger removals/restorations and both restricted factories. The complete
original tenant-schema, RLS, runtime-role and Reminder SQL gates also pass in
that invocation. Owned containers and environment files are independently absent.
The first expanded attempt fails before ledger testing because its helper's
68-character database hostname exceeds the 63-character DNS label limit. That
failed report is retained and cleaned up; the passing fresh invocation uses
validated shorter names. No test, assertion, migration count or deadline changes.

The original complete seven-case watch phase and five-case watch/read permission
ordering phase are now running together on clean schema 126. The unchanged
ordering scenarios use the expanded read-only persistence oracle; an additional
whole-graph check after successful completion compares every counter to its
retained source clocks. Their terminal result remains pending. The original
100,000-notification capacity script additionally compares exact counter clocks
before/after its thirty real read facts and includes the whole counter in
permission/refusal fingerprints. Its original data counts, timing samples,
thresholds and HTTP behavior remain; updated capacity execution is still required.
