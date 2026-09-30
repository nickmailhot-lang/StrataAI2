# Durable Organization job foundation (ARCH-07)

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

This is a foundation, not complete ARCH-07 acceptance: no handler is scheduled
yet, and there are no mailbox/AI/object-storage adapters or provider sends. Global
identity verification/reset delivery needs its own explicit identity scope and
safe token delivery design; it must not invent an Organization or bypass this
queue's RLS. Outbound effects still need provider idempotency because a lease
cannot prevent a provider send followed by a worker crash. Long jobs require
future bounded lease renewal or smaller steps before being enabled.

CI executes the actual queue functions with a non-bypass role: cross-tenant and
missing-scope denial, atomic rollback, duplicate publication, lease fencing,
delayed retry, terminal failure, successful completion, and crash recovery.
