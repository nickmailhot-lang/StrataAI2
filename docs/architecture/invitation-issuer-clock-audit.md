# Invitation issuer authority clock audit

This audit and bounded repair narrow FOUND-FR-009's remaining clock work. The
local migration and restricted-contract results below do not establish complete
PRD-01 acceptance. PRD-01 remains open at **34%
estimated work remaining** (planning estimate).

## Owning records and writers

| Relation | Allowed product writes and durable facts | Clock classification |
| --- | --- | --- |
| `invitation_issuer_authority_proofs` | The User status trigger inserts a same-transaction proof of deactivation, including canonical `users.updated_at` as `changed_at`. | Immutable transition fact; UPDATE/DELETE trigger rejects changes. |
| `invitation_issuer_authority_sources` | Identity-event publication inserts the proven event identity, version, correlation and event `created_at`. | Immutable source fact; UPDATE/DELETE trigger rejects changes. |
| `invitation_issuer_authority_effects` | Recipient delivery inserts a source/recipient deduplication fact with `ON CONFLICT DO NOTHING`. | Immutable effect fact; UPDATE/DELETE trigger rejects changes. It references its durable source rather than owning mutable lifecycle state. |
| `invitation_issuer_authority_jobs` | Source publication and bounded delivery insert PENDING jobs. Claim advances attempt count and leases; recovery reclaims expired leases or marks exhaustion FAILED; delivery marks SUCCEEDED and clears the lease. | Mutable operational record. Schema 114 lacked a general claim/reclaim update clock; migration 115 adds the managed clock described below. |

[Migration 109](../../db/migrations/109_invitation_issuer_account_authority.sql)
defines all four relations, the publication/claim/delivery functions, forced RLS
and history protections. [Migration 110](../../db/migrations/110_invitation_issuer_authority_exhaustion.sql)
replaces claiming with bounded fifth-attempt exhaustion and freezes both terminal
states. The immutable-fact protection comes from
[migration 105](../../db/migrations/105_invitation_recipient_authority.sql).

The [restricted Worker store](../../src/StrataAI.Infrastructure/Onboarding/PostgresInvitationIssuerAuthorityDeliveryStore.cs)
calls the claim function and executes delivery inside its owning transaction.
Its returned claim is a capability value, not an independently mutable stored
entity. [Runtime role provisioning](../../db/provision-runtime-roles.sql) grants
the Worker execution of those two functions, without direct table writes.

## Required repair and verification

The repair must record accepted claim, reclaim, failure and success clocks inside
the same transaction, preserve immutable identity/creation and terminal history,
and leave refused capabilities, repeats and rolled-back delivery unchanged.
Creation must initialize the clock without two independently sampled values.

Historical upgrade needs separate PENDING, RUNNING, SUCCEEDED and FAILED cases.
Completion/failure clocks are durable terminal facts; a lease deadline is not a
general mutation journal. Privileged test fixtures explicitly change deadlines,
so blindly subtracting a lease duration cannot establish historical provenance
for every existing row. Do not use upgrade time as an invented historical update.
Resolve and verify the treatment of unverifiable historical state before adding
a non-null constraint or claiming upgrade acceptance.

The [existing restricted issuer contract](../../tests/StrataAI.Persistence.Contracts/InvitationIssuerAccountAuthorityContract.cs)
already covers substituted capabilities, bounded 100/100/5 routing, late lease
failure rollback, reclaim, old-lease refusal, restricted table access and terminal
exhaustion. Extend those owning transitions with full before/after clock checks;
retain their original assertions. Its privileged deadline fault injection is
distinct from product clock provenance. The
[actual Worker delivery gate](../../scripts/ci/test-invitation-recipient-authority-delivery.sh)
also injects expiry and checks exhaustion stability. Neither existing test proves
the absent update clock.

This classification covers these four issuer relations only. Recipient counters,
pages, other leases, projections and remaining catalog candidates still require
their own writer and historical-state audit.

## Managed job clock and historical upgrade

[Migration 115](../../db/migrations/115_invitation_issuer_job_clocks.sql) initializes
`updated_at` from the same persisted `created_at` on insertion. Accepted changes
advance it inside the owning transaction, retaining monotonicity and known
terminal clocks. No-op updates preserve it; explicit clock replacement is refused.
Existing identity, source, creation and terminal-history protections remain.
The API and Worker gain no direct job-table grants or new function arguments.

The upgrade exclusively locks the job table and **refuses any legacy RUNNING
job**, without committing a column, ledger entry or changed job. Existing Workers
must finish or exhaust those jobs through their original capabilities before
the upgrade is retried; do not fabricate a completion or alter a deadline to
invent a historical clock. With claiming paused after that drain, PENDING rows
retain creation time and SUCCEEDED/FAILED rows retain completion/failure time.
The historical backfill and restoration of the terminal-history trigger are
atomic under the exclusive lock. Non-finite or contradictory clocks fail the
constraint rather than being replaced by migration time.

Both runtime readiness lists require 115. The complete migration gate now tests
legacy refusal with a complete-record fingerprint and unchanged column/ledger,
real restricted exhaustion, four preserved historical rows, repeat execution,
claim/reclaim clocks, refused capability, successful and exhausted terminal
clocks, full claim rollback, no-op stability and clock/terminal tamper refusal.
Its original clean/forward/serialization/injected failure/unrecorded checks remain.
See the [before](../../scripts/ci/issuer-job-clocks-before-upgrade.sql),
[drain](../../scripts/ci/issuer-job-clocks-drain.sql) and
[after](../../scripts/ci/issuer-job-clocks-after-upgrade.sql) fixtures.

On 2026-10-09 the complete gate passes on an owned PostgreSQL17/pgvector container.
The actual restricted issuer routing/recovery, recipient replay/discovery and
complete API/Worker ledger refusal/recovery contracts also pass against a fresh
schema-115 database using the locked compiled source. The Release solution build
has zero warnings/errors. Initial harness failures (nonempty clone, missing
fixture delivery grant and wrong fixture database selection) remain privately
retained; they are not passing executions or product defect claims. The final
gate and contract environments are removed independently of the ongoing Board
run. This is local source/persistence evidence, not current immutable-image CI,
deployed drain verification or clock coverage for the remaining candidates.
