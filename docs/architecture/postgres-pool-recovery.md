# PostgreSQL connection admission after restart

## First request recovery — 2026-10-15

The commands group in candidate `32236a3d` run `38805351041` passed the
operator-metrics database stop/start stage, then failed registration in
`test-work-events.sh` with HTTP 503. Its discarded response body does not prove
the exact hosted error category. A separate small native reproduction confirms
a relevant failure: the independent readiness pool reports recovery while the
application pool still contains stale PostgreSQL connections.

With fresh PostgreSQL 17.11, all 149 migrations and the canonical restricted API
role, the unchanged API process returns readiness 503 during database shutdown
and 200 after restart. Before repair, the first keyed registration then returns
`identity_storage_unavailable`, with zero users written. An explicit request with
the original key/body succeeds and stores exactly one user. This is diagnostic
evidence, not an acceptable automatic retry of a business command.

`PostgresConnectionFactory` now reconnects once after a transient Npgsql failure
while opening and verifying a connection. It clears only that data source's
pool, retires the failed connection, and repeats the server-version, safe-role
and complete-schema checks. No business query or transaction has started at
this boundary. A second failure propagates; stable role/schema refusals are not
retried, caller cancellation propagates, and existing readiness deadlines remain.
Business writes, commits and commands receive no automatic retries.

## Verification and limits

Four new protocol regression cases fail before repair. Afterward all nine cases
pass, covering one successful reconnect, a persistent transient failure limited
to two connections, role/schema/permission refusals, repeated guards after
reconnect and already-cancelled calls. The admission scenarios exercise both
application and readiness paths with real Npgsql against a bounded loopback
protocol peer. All eight server-version cases and three readiness deadline and
cancellation cases also pass. The full solution builds without warnings/errors.

The repaired native PostgreSQL restart rehearsal passes in **28.096 seconds**:
readiness 503 → 200, then the **first** keyed registration returns 201, stores
one user and one `USER_REGISTERED` event, and preserves the API process identity.
All 714 captured source/database inputs and 29 API binary files match before and
after execution. Its owned API, PostgreSQL, network and persistent volume are
removed, restoring the original Docker inventory. Existing local services and
the original database are unused. No image is built, downloaded or imported.

The mandatory exact-image operator-metrics fixture now also requires recovered
API readiness, first-attempt registration, exactly one stored user/event and an
unchanged API process after its existing database restart. It uses no client
mutation retry. Account bodies stay in temporary scratch files; existing retained
metrics evidence remains aggregate-only. Separate command-substitute cases check
driver ordering, failure refusal and cleanup; they do not replace real database
or immutable-image execution.

All 16 complete driver control-flow cases, 31 operator-metrics verifier cases
and 282 CI guard cases pass. The new refusal cases cover a failed first
registration, API process replacement, unrecovered API readiness, duplicate
users and duplicate registration events. Bash/workflow lint and documentation
navigation checks also pass (1,796 links and 521 anchors).

The native rehearsal mounts current compiled assemblies read-only in a retained
runtime. It does not certify a complete current release or establish the exact
cause of the historical hosted 503. Current immutable-image CI, release and
managed deployment acceptance remain required. This is a small recovery check;
large load testing remains disabled until after the operational MVP milestone.

See [operator metrics](operator-metrics.md),
[runtime modes](runtime-modes.md) and the
[operational MVP milestone](operational-mvp-milestone.md).

## Complete Work event scenario after restart — 2026-10-15

The full unchanged [Work-event driver](../../scripts/ci/test-work-events.sh)
passes after a real PostgreSQL stop/start with current compiled API/Worker
assemblies from `d635b54d97c61de981cda7a6d5d48651b93f92d1`. This is the complete
scenario whose first registration fails in the older hosted commands job,
extending the earlier first-registration check to its downstream contracts.

The **118.472-second** native run uses fresh PostgreSQL 17.11, all **149
migrations**, the canonical restricted runtime roles and a separate Worker.
Registration/login first warm the application pool. Only that owned database is
stopped and restarted, preserving its volume; readiness reports 503 → 200.
The same API process then executes the driver without automatic HTTP retries.
The driver's first registration succeeds, leaving exactly one fixture account.

The unchanged assertions verify:

- Board/List/Card creation, three initial content-free events and corresponding
  correctly attributed delivery jobs, and exact original-key Card replay.
- Revoked event/queue INSERT privileges each produce HTTP 503 and roll back the
  Card version, event sequence, event rows, audit and replay claim. Restored
  permissions allow one recovered edit; replay does not duplicate it.
- Concurrent Card creation produces six contiguous Board events; cross-tenant
  reads return no rows and a cross-tenant stream insert is refused by RLS.
- Restricted Worker columns and API/Worker mutation privileges stay constrained.
  The Worker completes six jobs and publishes six readiness effects.
- Reclaim after a simulated post-effect crash preserves the original readiness
  timestamps and unrelated Organization metadata jobs/events exactly.
- Actor, Board and tenant substitution jobs are refused without publishing the
  protected readiness effect. Forced lease expiry rolls that effect back; normal
  later recovery succeeds after the fault trigger is removed.

The fixture's base Compose configuration mounts the **71 current API/Worker
binary files read-only** in cached runtimes. The canonical Work-event scope
overlay and driver are unchanged; a host adapter supplies UUIDs for Git Bash's
missing Linux UUID file. Real HTTP, SQL, PostgreSQL RLS, Worker jobs, permission
changes and lease triggers are used. IntegrationTest explicitly permits local
transport, open signup and unverified accounts for this commands fixture; this
does not establish strict production verification or managed mail acceptance.

All **716 source/database/driver inputs** and **71 binary files** match the
captured values afterward. The API process remains unchanged. Owned API/Worker/
PostgreSQL containers, persistent volume, network and **4,119 temporary bytes**
including fixture credentials are removed; original Docker inventory is restored.
No existing local service/database is used, application image is built/pulled/
imported, provider is called or large-load test is executed.

This proves current compiled-source behavior for the full scenario, not the
current immutable application images. It does not establish the exact cause of
the historical response whose body was discarded. Current exact-commit CI and
complete release acceptance remain mandatory. PRD-01 and PRD-02 stay open at
**34%** and **16% estimated work remaining**, respectively (planning estimates).
