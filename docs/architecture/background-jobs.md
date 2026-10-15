# Durable Organization job foundation (ARCH-07)

Account deactivation also has a separate private, subject-RLS
[issuer authority routing queue](invitation-recipient-authority.md#canonical-issuer-account-deactivation).
Its Worker-only capabilities lease one bounded routing page, then publish the
ordinary tenant authority roots. It grants no direct global table access and
does not widen the general Organization job claim path. Completed private page
history is immutable; each continuation retains its own five-claim limit.
Forward migration 110 retires an expired fifth issuer-routing lease as FAILED
with the stable `LEASE_EXHAUSTED` reason and finite failure timestamp. One claim
call examines/changes at most one row, returning no delivery capability when it
retires that row. Subsequent calls can claim the next source. A live final lease
is still protected; completed and failed history cannot be reset, deleted or
rewritten. Runtime roles have no direct reads/writes of this private queue.
This dead-letter state requires operator investigation; automatic unlimited
retry or privileged attempt resets are not a supported recovery procedure.

Migration 007 and `PostgresBackgroundJobStore` provide a PostgreSQL queue without
an additional broker. Infrastructure producers publish using their existing
`TenantDbSession`; domain writes and job publication commit or roll back together.
Organization/type/idempotency-key uniqueness retains the first publication. Keys
must include the approved entity version when retrying versioned correspondence.

Every Organization job carries actor/service identity and correlation metadata.
Forced RLS, explicit Organization predicates, and transaction context apply to
claim/completion/failure. Functions run as the invoker, never a security-definer
or tenant-bypassing worker. The C# publisher additionally rejects mismatched
Organization context even with a privileged development connection.

Claims use [PostgreSQL SKIP LOCKED](https://www.postgresql.org/docs/17/sql-select.html)
and a fresh two-minute lease. The claim transaction commits before provider work.
Completion/failure requires the current unexpired lease and worker identity;
expired or superseded workers cannot acknowledge a job. Default retries stop
after five claims (configurable on publication from one to ten), with 30-second
exponential backoff capped at an hour. A final-attempt crash becomes FAILED when
the next claim pass recovers expired leases. FAILED is the dead-letter state;
there is no automatic unlimited retry. Operators must not manually reset a job
unless the provider idempotency contract and approved version still permit it.

Metadata holds references and safe routing data, capped at 32 KiB. Documents stay
in object storage. Passwords, provider credentials, plaintext reset/invite tokens,
and message bodies must never be placed in this metadata. Provider error bodies
must not be persisted; failure accepts a bounded stable error code only.

The separate Worker now hosts the application dispatcher when explicitly scoped
with `STRATAAI_WORKER_ORGANIZATION_IDS` (comma-separated, nonempty UUIDs, maximum
100 unique Organizations). Missing scope disables this general-job loop;
invalid IDs, Demo execution or enabled scope without handlers fail startup.
Database grants must restrict this service identity; configuration is not a
replacement for database authorization. No Organization discovery or bypass of
RLS occurs in this explicit general-job loop. Production runtime registers the
PostgreSQL job store. Accepted Organization deletion has a separate
[automatic routing capability](organization-deletion-lifecycle.md#automatic-production-deletion-discovery):
bounded UUID hints from canonical deletion roots, followed by the same explicit
tenant RLS, invoker queue leases and graph proof. That capability grants no
global job/graph reads or claim/mutation authority. It is enabled by default in
Production and can be suspended independently of the general job scope.

Handlers declare their job type and service identity. Dispatch verifies the
claimed Organization, worker and actor, then matches the handler's service
identity. Duplicate handler registrations fail. Execution receives cancellation
five seconds before the lease deadline. Provider failures store only stable error
codes; shutdown leaves the lease to be recovered. An acknowledgement rejected by
PostgreSQL is reported as lost, never successful. Unknown handlers/services retry
within the existing attempt limit. Handlers must honor cancellation and use
provider idempotency; cancellation cannot undo an already completed external send.
Worker outcome logs include job/Organization/actor/service/worker IDs, type,
attempt and correlation ID, without metadata, token, message or exception bodies.

## Optional outcome diagnostics isolation (2026-10-15)

`BackgroundJobProcessor.Report` previously allowed the optional diagnostics
callback to throw after the store had already decided completion, retry or
lease loss. A successful acknowledgment could therefore appear to the caller
as an exception. Diagnostics are not the authoritative queue state and must
not replace that decision or imply another execution is needed.

Only the optional observer invocation is now isolated. Its exception, including
an observer-generated cancellation exception, cannot change the already chosen
result. No observer exception body is persisted or recursively logged. Handler
execution, caller cancellation, store errors, lease checks, finite attempts,
safe failure codes and actual acknowledgment semantics remain unchanged.

Eight regressions fail on the prior processor: successful completion, handler
failure, rejected completion acknowledgment, rejected failure acknowledgment,
expired lease, missing handler, wrong service, and observer cancellation after
completion. Recording store/handler doubles assert the exact expected execution,
completion and failure-call counts plus safe error code, claimed job and outcome.
They establish this control-flow boundary, not independent PostgreSQL durability.
All 13 existing processor cases continue to pass, including genuine shutdown,
provider timeout, scope refusal and rejected acknowledgment.

The unchanged baseline has **21 total / 8 failed / 13 passed**. The repaired
complete class passes **21/21 in 0.395 seconds**, zero errors, skips or unexecuted
cases, using the native xUnit runner in Linux .NET 10.0.13. The current compiled
test output is mounted read-only into the retained API runtime image with no
network; no application image is rebuilt, downloaded or imported. Captured
source and compiled-output hashes match, owned test containers are removed and
the full Docker inventory is restored. The full solution builds with zero
warnings/errors. Current complete immutable CI, durable queue/provider
acceptance and the remaining ARCH-07/08 scope still govern closure. No existing
database or large-load test is used by this scoped verification.

### Logging failure during the Organization loop (2026-10-15)

The processor boundary alone does not protect the outer Worker loop. Its pass
log could throw after a completed job, enter the error handler, then encounter
another exception from the warning logger. A warning-provider failure after a
claim error could also escape. Either path faults the background service before
the next Organization is considered.

The loop now isolates its pass and warning logging calls independently. Message
templates and safe diagnostic fields are unchanged. A logger error does not
generate another warning through the same broken provider. The 32-job/250 ms
fairness bounds, one-second polling interval, actual processor/lease semantics
and cancellation/shutdown behavior remain intact. This handles thrown logging
failures; it does not claim to bound a provider that blocks indefinitely.

Three lifecycle cases compile the actual `OrganizationJobWorker.cs` into the
API test project through an explicit source link, avoiding the separate host
entry point and adding no package or runtime-host dependency. They start and
stop the real `BackgroundService` with two scoped Organizations and recording
store/handler adapters. After successful work, a handler failure or a claim
failure, a throwing logger must not prevent the second Organization's job from
completing. Exact execution/acknowledgment/failure counts, stable failure codes,
no false failure warning, safe formatted messages and successful shutdown are
asserted. The second scope's drain signal occurs after its pass log, so an early
completion signal cannot conceal a later logger failure.

All **three cases fail before repair**, with the actual loop faulted before the
second scope drains. After repair, **3/3 pass in 0.281 seconds**, zero errors,
skips or unexecuted cases, in Linux .NET 10.0.13. Current compiled test output is
mounted read-only into the retained runtime image with no network. All four
captured sources and compiled-output hashes match; owned containers are removed
and full Docker inventory restored. The full solution builds with zero
warnings/errors. These are actual loop lifecycle/control-flow tests with store
doubles, not full Worker-image, PostgreSQL durability or deployed logging
acceptance. Current immutable CI and remaining ARCH-07/08 requirements remain
necessary. No image build/download/import, existing database use or large-load
test occurs.

The production Worker registers `WORK_EVENT_READY` under `work-event-delivery`.
Its handler locks and checks the current unexpired queue lease before marking a
content-free Work event ready. Replay preserves the original readiness timestamp.
Migration 011 gives API append access and Worker access only to event references
and readiness; the Worker cannot read event type, version or domain content.
Mailbox/AI/object-storage adapters remain pending, so ARCH-07 is incomplete.

The Worker also registers [Organization metadata delivery](organization-metadata-events.md)
as `ORGANIZATION_METADATA_EVENT_READY` under `organization-metadata-delivery`.
The reference-only job is published atomically with its source audit/event and
marks readiness under an initial and final lease fence. Historical actor
departure does not strand committed events; fresh reader authorization remains
required. Production metadata routing is enabled by default with
`STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=true` and needs no explicit
scope list. Its dedicated invoker queue claim leaves other job types untouched.
Authorized live consumption is still pending.

Work mutations publish their audit, event, board sequence and queue job inside the
owning command transaction. The publisher refuses standalone transactions.
Sequences are scoped per board and rollback without gaps; keyed command replay
does not append again. Demo retains envelopes in memory without durable delivery.
The [replay API](work-synchronization.md) freshly authorizes Board access and
advances only through contiguous ready sequences; a later ready event cannot skip
an earlier pending one. The SignalR server streams these authorized pages, and the
Board client recovers cursors and refreshes authorized snapshots automatically.
Global identity verification/reset delivery now has its own scoped queue and
Resend provider under [identity-email.md](identity-email.md); it does not invent
an Organization or bypass this queue's RLS. Outbound effects still need provider
idempotency because a lease
cannot prevent a provider send followed by a worker crash. Long jobs require
future bounded lease renewal or smaller steps before being enabled.

CI executes the actual queue functions with a non-bypass role: cross-tenant and
missing-scope denial, atomic rollback, duplicate publication, lease fencing,
delayed retry, terminal failure, successful completion, and crash recovery.
Application tests additionally cover dispatcher scope/service rejection, leased
completion, shutdown, deadline cancellation, safe provider errors and lost leases.


## Demo queue audit state

The process-local [Demo store](../../src/StrataAI.Infrastructure/BackgroundJobs/InMemoryBackgroundJobStore.cs)
now retains `CreatedAt`, `UpdatedAt` and a revision for each mutable queue record.
New publication captures one actual clock instant for both timestamps and starts
at revision 1. An admitted lease claim/reclaim, retry, completion or final expired
lease retirement captures its actual mutation instant and increments the revision
once. Creation time remains the first publication's time; schedule and lease
expiry do not substitute for audit times. Duplicate/terminal publication, foreign
Organization/Worker/lease completion, canceled claim and exhausted-terminal reads
leave the audit state unchanged. Existing transaction snapshots restore clocks and
revision along with payload/lease state. No public diagnostics route or provider
is added; these are process-local metadata, reset with the owning API process.
Existing two-minute leases, five attempts, tenant/work gate and idempotency remain.

Before repair, the complete queue invariant class passes 7/10: all seven previous
cases pass and the three new audit cases fail on missing stored fields. After
repair, all three complete queue, reminder/actual-transaction and runtime-composition
classes pass **17/17**, with 17 unique executions and matching counters, zero
failed/error/timeout/aborted/pending/unexecuted results, in the pinned .NET 10.0.12
Linux runtime with real source/content-root mappings. The locked solution build
passes with zero warnings/errors. Private reports:
`demo-job-audit-baseline-native-20261010/api.trx` and
`demo-job-audit-focused-native-20261010/api.trx`. Owned test containers are removed
after terminal results. Test observation reads actual private stored metadata;
lease replies alone would not prove refused/no-op state preservation.

The complete unfiltered API suite on the repaired backend subsequently passes
**714/714 actual unique tests and executions**, with matching declared counters
and zero failed/error/timeout/aborted/pending/unexecuted or other non-passed rows.
The same pinned Linux runtime and real source/content-root mappings are used;
the owned test container is independently absent. Private terminal report:
`demo-job-audit-full-api-native-20261010/api.trx`. This is the repaired backend's
full proof; the prior 711-case binary is retained separately. Current immutable-image
CI and all Demo handlers/future module acceptance
remain required. This prospective process-local repair does not reconstruct
legacy PostgreSQL audit provenance or satisfy full FOUND-FR-009 by itself.


Configuration delivery uses the dedicated `ORGANIZATION_CONFIGURATION_EVENT_READY` / `organization-configuration-delivery` handler and `STRATAAI_ORGANIZATION_CONFIGURATION_DISCOVERY_ENABLED` setting. It preserves the committed source actor/correlation, checks lease/source authority before and after first readiness, and routes without preconfigured Organization IDs. The typed claim leaves unrelated provider work untouched; the Worker has no private configuration-history read grant. See [delivery behavior, executed evidence and remaining recipient acceptance](organization-configuration.md#durable-configuration-worker-delivery). Configuration Activity and authorized replay remain separate acceptance work.
