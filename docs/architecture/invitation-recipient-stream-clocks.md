# Invitation recipient stream clocks

[PRD-01 FOUND-FR-009](prd-01-acceptance.md) includes the mutable
`invitation_recipient_streams` publication counter. Migration 123 adds
source-derived `created_at` and `updated_at`: the first published event's
retained creation time and the maximum retained source creation time.
These are journal projection clocks, not inferred account creation times,
delivery dates or migration wall time. Equal source timestamps may retain an
unchanged update clock while sequence advances.

## Provenance and privacy

Migration 102 introduces the counter and immutable recipient event journal.
The active writer replacement in migration 104 increments the recipient
counter and appends its proof-backed event in one transaction. Events have
positive unique recipient sequences, finite immutable creation timestamps and
retained audit/proof references. Migration 103 cleans only never-published
proofs; its published-event check and foreign keys preserve actual history.

The recipient key intentionally spans Organizations. Existing lookup RLS
admits only the protected recipient route kind and matching normalized email.
The repair leaves those policies, reader/cursor binding, event envelopes and
runtime grants unchanged. Private trigger functions use fixed search paths
and revoke PUBLIC execution; no direct counter/event write capability is
granted to API or Worker.

The upgrade locks both tables and refuses incomplete/gapped counter history
before adding any columns. It backfills from the first event and maximum
source timestamp, preserving all existing counter fields. An event trigger
refreshes clocks in the publication transaction. Counter defaults are only
provisional before the event append; a deferred constraint requires the final
counter clocks and last sequence to match retained sources before commit.
Like the [Organization stream repair](organization-metadata-stream-clocks.md),
the guard reads the final row rather than provisional queued NEW values.
Existing sequence keys and the source-time index bound lookups. API/Worker
startup requires ledger `123_invitation_recipient_stream_clocks`.

## Verification scope

The complete migration gate creates an actual proof-backed invitation
publication and an unbacked recipient counter. Refusal must preserve the full
counter fingerprint and leave both columns and ledger 123 absent; removing
only the unbacked fixture allows upgrade/repeat. Historical fields/clocks are
compared. Rolled-back checks force deferred constraints to reject creation or
update clock replacement and source-less allocation, retain no-op clocks,
and verify proof-backed revocation preserves creation and advances clocks to
the canonical invitation revision time.

The initial revocation fixture expected the supplied revocation date to be
the update clock. The existing invitation revision guard instead records its
own persisted update time. That failed report is retained privately; the
fixture now compares the actual canonical revision fact. Product guards and
clock semantics were preserved.

The original restricted recipient replay/authority contracts run against
schema 123, including complete API/Worker required-ledger readiness. A
separate database check compares every resulting recipient stream with its
first/max source times and maximum sequence. These compiled local fixtures
stage accounts/trusted application context; they do not prove browser/provider
or current immutable-image release acceptance. The complete schema-121 Board
browser phase remains separately active.

The corrected complete local migration gate and the original restricted
recipient replay/authority/full required-ledger readiness contracts pass.
Historical body/clock comparison, atomic missing-history refusal, no-op/tamper/
unbacked-allocation and canonical revocation checks pass. The locked Release
build reports zero warnings/errors, and all 179 integration/build metadata
source checks pass without skips. Independent source/staging, cleanup and local
documentation target checks accompany the retained private reports. Owned gate
and contract containers/credential files are removed; original services and the
live schema-121 browser phase are preserved.

The distinct `invitation_recipient_authority_revisions` counter still has
unresolved historical mutation-clock provenance, as recorded in the
[authority audit](invitation-recipient-authority.md#page-lifecycle-clocks-and-remaining-counter-audit).
Other mutable-record and release gaps also remain. PRD-01 stays open at
**34% estimated work remaining** (planning estimate).
