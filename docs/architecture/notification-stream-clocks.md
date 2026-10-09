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

The combined twelve-case phase subsequently finishes **7/12**, with five setup
failures in the watch/permission/read ordering files, zero skips or flaky outcomes
and no report errors. All seven original watch cases pass; this is not a complete
ordering pass. Those five failures stop at the Board-lock gate's echo check:
the local shim redirected synchronous Docker calls but omitted `spawn`, so the
real gate subprocess did not reach the owned database. A fresh complete
twelve-case invocation uses the original proven spawn-aware shim and retains
every original wait/peer-blocker assertion, command pair and deadline. Its
result and final whole-counter check remain pending. The failed phase's
database/environment files are independently absent; its private report remains.

The first local capacity attempts fail in native file handling before consumer
acceptance, and remain retained separately. Git Bash prepends its bundled curl
ahead of the injected Windows PATH; explicit child-shell binding selects the
owned adapter. Curl output and native jq input then require explicit MSYS drive
path translation. Both adapters now pass a real file input/output preflight
before a fresh whole activity/notification run. No data count, assertion, sample,
HTTP deadline or p95 budget changes. The full graph still contains 200 Lists,
5,000 active Cards and 100,000 archived Cards; its synthetic activity and
notification scale histories are separate from evidence of audited generation.

The canonical activity/notification scripts now accept an explicit base URL and
private report directory. CI defaults retain the existing release URL, report
paths and exact-image topology. Local compiled reports explicitly use
`local-compiled-runtime`, null release `revision` and a separate `sourceRevision`.
They cannot be counted as immutable release-image proof. Current capacity
execution, fresh ordering completion and exact build-once CI remain required.

The current local capacity invocation completes the entire original activity
script on 200 Lists, 5,000 active Cards, 100,000 archived Cards and 100,000
synthetic activity sources. Both Board and Card history retain all twenty
samples, bounded unique pages, body-free results and unchanged read-state
fingerprints. Independent report inspection verifies declared counts and
nearest-rank p95 values: **2,856.42 ms** for Board history and **1,256.89 ms** for
Card history. These are local capacity history-read measurements, not initial
Board rendering, mutation acknowledgment or normal-data budget certification.
The report explicitly has null release revision and local compiled topology.
The subsequent original 100,000-notification insertion is confirmed executing
in its isolated database; full notification consumer/clock/p95 acceptance remains
pending. Its live handle is preserved.

Evaluating both actual old/new report expressions with deterministic reporter
inputs confirms default CI objects are unchanged, and local output uses null
release identity plus separate source identity while retaining every fixture,
verification and sample field. Those checks verify formatting/provenance only;
their inputs are not runtime performance evidence. Both Bash scripts parse,
and all 234 Node source cases pass with no skips.

The spawn-aware twelve-case invocation subsequently finishes **10/12** in
1,411.58 seconds, with one result per case and zero skipped/flaky cases. All five
observed lock-order scenarios pass. MEMBER and PUBLIC_READER cross-Board cases
fail at the third delivery: their native inbox refresh receives a 404 and retires
its display. This is a product failure, not the earlier subprocess setup problem.
The private failed report is retained; its owned containers, database and
credential files are independently absent. The final whole-counter check did
not execute because the browser phase failed.

Inbox, synchronization and read-command admission previously compared the
discovered Card route with its fresh route after acquiring Board locks. A move
committing during that wait could make a valid recipient look unavailable.
`NotificationInboxService` now releases the entire failed transaction before
rediscovering the route and reacquiring the complete sorted Board lock set.
Source identity, visible-row equality, fresh Board permissions and actor checks
remain required. No unlocked destination is accepted, no extra Board is locked
out of order, and no command effects or replay receipts survive a failed attempt.
Three changing discoveries return transient storage unavailability; actual
permission withdrawal still fails closed.

The complete locked solution builds with zero warnings/errors. All **six** new
API regression cases pass, covering inbox/sync/read for one stale discovery and
continuous movement, unchanged source identities/clocks, atomic read behavior,
canonical current links and actual subsequent Board-access withdrawal. These
tests project a formerly valid discovery over real moved-Card data; they do not
certify native database interleaving. A fresh full original twelve-case native
invocation is running against the repaired API and separate Worker. Every
original scenario, assertion, deadline and the final whole-counter oracle is
retained. Its terminal result remains required, as do full current-source tests,
the still-running capacity invocation and current immutable build-once CI.

The repaired complete twelve-case invocation subsequently finishes **11/12**
in 1,230.01 seconds, with one result per case and zero skipped/flaky outcomes.
All five cross-Board recipient roles pass, including the two previously failing
roles; all five observed command-order scenarios also pass. The remaining
personal-watch control scenario fails at `watch-subscriptions.spec.ts:89`: after
the lost-response check and native retry gesture, the recovery control remains
present. That is not a complete phase pass or proof that the control failure is
resolved. Its failed trace/report are retained. The final whole-counter check
does not execute after browser failure. Owned containers, database and credential
files are independently absent.

Migration 127's [retained journal history repair](notification-event-history.md)
is a separate subsequent change with complete upgrade/refusal, all-127-ledger
readiness and security proof. Its native producer/consumer run is still live;
the eleven-case result above uses the original repaired schema-126 build and
cannot certify schema-127 or immutable-image release acceptance. The personal
watch retry failure remains an explicit next verification gap.

Read-only reconstruction of the retained watch trace's incremental DOM
snapshots establishes that the retry button is disabled both before and after
the focus call, and during the native Enter action. It becomes enabled only
afterward. The next test version explicitly requires enabled and focused state
before the same Enter gesture. Original request key/body equality, exactly two
writes, recovery-control removal, source-clock persistence, movement/lifecycle,
accessibility assertions and deadlines remain. Browser typecheck passes. A
fresh complete original twelve-case invocation is running on schema 127;
its terminal result and whole-counter check remain required. This input
observation repair does not change `WatchControl` product behavior or rewrite
the earlier failed phase as a pass.

## Terminal schema-127 watch/order and capacity results

The enabled/focused-input invocation finishes **12/12** in 1,257.23 seconds
on 2026-10-09, with exactly one result per original case, zero skipped/flaky
outcomes and zero report-level errors. It preserves all five cross-Board roles,
the watch matrix, personal watch recovery and five command-order cases.
The successful-exit oracle then checks the whole retained counter graph against
sequence-one/greatest source clocks. Its owned containers, database and
environment files are independently absent. The API and Worker use schema 127;
frontend assets are the frozen pre-comment-focus build. This is local runtime
proof for that scope, not immutable-image or whole-PRD acceptance.

The original full-count capacity invocation finishes with exit **26** after
native curl cannot open the concurrent readers' `--data-binary @file` payload
under disabled MSYS path conversion. The existing adapter translated cookie
and output paths but omitted payload paths. The activity report remains a
separate completed result; no notification capacity pass or p95 acceptance is
claimed from the failed invocation. Its owned runtime is cleaned up and its
private diagnostics remain retained.

A fresh full invocation uses schema-127 API assets and extends only the private
Windows adapter to translate those payload-file arguments. A local HTTP
preflight verifies exact payload bytes for all four supported curl data-file
options. Original scripts, 100,000 notifications, graph size, sample counts,
concurrent readers, replay checks and the p95 <500 ms assertion are unchanged.
The new invocation's terminal report and current build-once CI remain required.
