# Protected invitation mail storage

Migration 023 adds one protected mail intent per Organization invitation. Composite
foreign keys bind its job and invitation to the same tenant and issuer. The
immutable snapshot captures recipient, surface, role, expiry, signing key, sender,
public origin, provider account and template version. It stores no bearer token.
The API may insert/read tenant-visible intents, but cannot change delivery state.
The tenant Worker cannot directly read or modify the ledger or global users.

Two narrowly granted database capabilities load an envelope and acknowledge a
terminal result. They require the actual tenant context and matching job, issuer,
worker, lease, handler identity, exact reference-only metadata, RUNNING state and
unexpired database-clock lease. Loading also checks the canonical invitation,
active Organization, current administrative issuer membership, active issuer
account, configured email verification requirement and Owner grant restriction.
Revocation, acceptance, expiry or snapshot divergence makes the envelope unusable.
Historical terminal state remains readable under a new admitted retry claim so
the handler can finish generic job acknowledgment without sending again.

These functions are SECURITY DEFINER capabilities owned by the migration role.
Their owner may bypass forced RLS; explicit tenant predicates and denied PUBLIC
execution are therefore part of the security boundary, independently of RLS.
Only the restricted tenant Worker receives execution permission. No global
account fields are returned and it retains no SELECT grant on users.

Acknowledgment locks the protected ledger and job before checking the current
lease with the database clock. A lease changed or expired during a lock wait
cannot persist a receipt. Terminal receipts are immutable under runtime grants.
The adapter closes its short transaction before the application handler calls
the mail provider. It persists SENT before generic job completion.

The store and handler are not registered and the API does not publish mail jobs
yet. Transactional publication, feature configuration, Worker registration and
exact API/Worker image delivery tests remain required before activation. Schema
readiness requires all migrations through 023 and runtime roles must be
reprovisioned after migration. CI includes repeat/upgrade/rollback checks and real
restricted-login capability, lease wait and immutable receipt tests. Local build
and script/configuration checks are not evidence of PostgreSQL execution.
