# Invitation issuer authority clock audit

This source audit narrows FOUND-FR-009's remaining clock work. It is not an
executed migration or a PRD-01 acceptance result. PRD-01 remains open at **34%
estimated work remaining** (planning estimate).

## Owning records and writers

| Relation | Allowed product writes and durable facts | Clock classification |
| --- | --- | --- |
| `invitation_issuer_authority_proofs` | The User status trigger inserts a same-transaction proof of deactivation, including canonical `users.updated_at` as `changed_at`. | Immutable transition fact; UPDATE/DELETE trigger rejects changes. |
| `invitation_issuer_authority_sources` | Identity-event publication inserts the proven event identity, version, correlation and event `created_at`. | Immutable source fact; UPDATE/DELETE trigger rejects changes. |
| `invitation_issuer_authority_effects` | Recipient delivery inserts a source/recipient deduplication fact with `ON CONFLICT DO NOTHING`. | Immutable effect fact; UPDATE/DELETE trigger rejects changes. It references its durable source rather than owning mutable lifecycle state. |
| `invitation_issuer_authority_jobs` | Source publication and bounded delivery insert PENDING jobs. Claim advances attempt count and leases; recovery reclaims expired leases or marks exhaustion FAILED; delivery marks SUCCEEDED and clears the lease. | Mutable operational record. `created_at`, `completed_at` and `failed_at` exist, but there is no durable general update clock for claim/reclaim transitions. This remains an established source gap. |

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
