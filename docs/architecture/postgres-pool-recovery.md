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
