# Work Management command transactions

ARCH-03, ARCH-04 and PRD-04 through PRD-09 require persisted mutations and audit
history to agree. Production Work Management now composes the application service
with `TransactionalWorkManagementService` and `IWorkManagementUnitOfWork`.
Resource routing establishes the organization; command authorization, validation,
mutation and the existing audit append execute in one PostgreSQL transaction.
Failed operation results and exceptions dispose that transaction without commit.
The HTTP endpoint cannot acknowledge success before the final commit completes.

The connection factory shares the owning session only down that command's async
execution context. Repository sessions borrow its connection/transaction, so
their existing commit/dispose calls cannot commit or close the command early.
Independent concurrent commands have distinct scopes. A command cannot switch
organizations or nest another command transaction. Same-board movement validates
its destination before entering the scope; cross-organization movement remains
unavailable. RLS retains transaction-local organization context throughout.
Standalone queries and existing non-Work-Management commands retain their original
session lifetime. Work routing reads borrow the command connection when present;
otherwise they use their own restricted connection. A command never reserves a
second pooled connection while holding its transaction, avoiding pool starvation
and making its own uncommitted routing writes visible to subsequent queries.

Database failures return a sanitized `work_storage_unavailable` 503 with the
existing correlation header. Logs record only organization and database error
code, never SQL or entity content. A lost commit acknowledgment can have an
unknown outcome: refresh before retrying. This increment does not yet provide
mutation idempotency or durable domain-event publication. The existing personal
star command has no audit append; this wrapper does not invent an activity event.
Demo mode retains its memory-only implementation and is not a production
transactional durability proof.

Build-once CI runs `scripts/ci/test-work-command-transactions.sh` against the exact
release API image and restricted PostgreSQL role. Denying audit INSERT forces
failures after card edit/create, list create, board create/archive, card move,
card archive and board-role change/removal; snapshots, membership and discovery
verify all those mutations rolled back.
The fixture verifies the audit count stayed unchanged, restores permissions,
checks a successful write/audit pair, and rejects a stale version without losing
the accepted update. A temporary audit delay forces overlapping commands in two
organizations, verifying isolated results and commits. It also starts the same
API image with a one-connection pool and verifies commands finish without waiting
for an unavailable second connection. It restores the API configuration, permissions
and removes temporary trigger/function fixtures even on failure, and refuses to
run outside CI. No fixture is included in release provisioning.

Still required: applying atomic command boundaries to organization/onboarding and
other modules, idempotency, audit/event envelopes and transactional outbox
publication, parent-state locking, realtime delivery/recovery, and the other
ticket acceptance criteria. These tests prove this transaction boundary, not full
completion of the dependent PRDs.
