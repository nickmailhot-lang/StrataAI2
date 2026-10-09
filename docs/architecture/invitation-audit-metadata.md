# Canonical invitation audit metadata

FOUND-FR-009 requires update clocks on mutable entities. Migration 099 already
stores invitation `updated_at` and `version`, initializes creation metadata,
advances genuine changes and preserves no-op revisions. Its acceptance and
revocation source proofs use those authoritative fields. This repair extends the
canonical `InvitationRecord` projection rather than adding another database clock.

PostgreSQL creation, original creation replay, active token/recipient-ID reads,
acceptance and owning canonical reads now carry the stored update clock and
revision. Acceptance returns the complete `UPDATE RETURNING` row, including its
stored timestamp precision, rather than combining the old record with the
caller's local acceptance time. Demo records initialize creation metadata and
advance it on actual acceptance/revocation; existing revision journals and rollback
participants remain in place. Public create/accept/history DTOs, routing grants,
token policy, migrations and event identities are unchanged.

The new [API-host regression](../../tests/StrataAI.Api.Tests/InvitationAuditMetadataTests.cs)
fails against the original projection because creation omits the update clock.
It then checks creation, accepted state, consumed-token refusal, original-ID
acceptance recovery, revocation and unchanged metadata after refused repeats in
the Demo store. This direct service case has explicit actor/clock fixtures;
separate existing HTTP admission and recovery cases remain required.

The ordinary mandatory PostgreSQL suite now includes the
[restricted metadata contract](../../tests/StrataAI.Persistence.Contracts/InvitationAuditMetadataContract.cs).
Its focused argument is `--invitation-audit-metadata-only`. Actual restricted
creation and receipt persistence, both routing reads, canonical/replay reads,
acceptance and revocation are compared with the primary stored row. A deliberately
sub-microsecond acceptance argument must not replace the database's authoritative
returned clock. Refused repeated transitions and owning revocation rollback retain
the original metadata. Accounts and Organization setup are fixtures; current
account admission uses the production verifier's trusted in-process context.
No HTTP session, mail delivery or Worker transport is claimed by this contract.

On 2026-10-08, the corrected native invocation passes against PostgreSQL
17/pgvector schema 114, including API/Worker missing-ledger refusal and restoration.
The first attempt is rejected by the intentionally non-authorizing persistence
fixture before invitation creation. The next reaches acceptance and detects a
returned-versus-stored row mismatch; returning the complete stored row repairs it.
Both failed attempts are retained separately. An intermediate Demo test incorrectly
expected a consumed bearer to remain usable; it now retains that refusal and tests
the implemented original-ID recovery path separately. Original token semantics
are not changed. Owned database containers are removed and original services and
volumes remain intact.

All 75 selected invitation API-host cases pass in the final invocation; the strict
locked Release solution build has zero warnings/errors. This is scoped compiled
source/store evidence. Current retained-image HTTP/browser/Worker, full source and
release gates, complete role/lifecycle matrices and other PRD acceptance remain
required. PRD-01 remains open at **34%**, PRD-03 at **8%**, and PRD-60 at **18%**
estimated work remaining (planning estimates).

The current exact-source run [37834908422](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37834908422)
at `9e6843c7` now completes its PostgreSQL integration job successfully. Its
retained log records the actual restricted invitation metadata contract passing
at 2026-10-08 19:52:07 UTC, including creation, routing reads, original replay,
accepted stored-row return, revocation, refused repeats and rollback. Clean/repeat,
forward upgrade, serialized migration runners and failure rollback also pass.
This establishes the mandatory current-source database gate; the separate
immutable-image HTTP/browser/Worker and required release gates still govern closure.

The [route clock extension](entity-route-clocks.md#invitation-routing-clocks)
in migration 119 now projects these canonical clocks into invitation routing.
The expanded real restricted metadata contract passes creation, acceptance,
revocation, refused repeats and owning rollback with exact route/canonical
clock equality. A fixture Organization rename preserves the retained
publication label and both invitation clocks. Complete current required-ledger
readiness, the full local migration gate and all five routing discovery-write
denial cases pass; immutable-image release/deployed upgrade proof remains.
