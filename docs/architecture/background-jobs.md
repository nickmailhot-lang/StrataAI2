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
