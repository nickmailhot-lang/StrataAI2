# Organization metadata stream clocks

[PRD-01 FOUND-FR-009](prd-01-acceptance.md) includes mutable journal counters.
Migration 122 adds `created_at` and `updated_at` to
`organization_metadata_event_streams` from its retained event history.
Creation is the first journal entry's recorded creation time. Update is the
maximum recorded source creation time in that stream. These are source-derived
projection clocks; they are independent of later event delivery/readiness.
Equal source times may produce an unchanged update clock on a new sequence.

## Historical provenance and writers

Migration 094 introduces the counter and immutable event journal together.
Its Organization metadata writer and the member/invitation writers in
migrations 097–101 allocate one sequence and append one event in the same
transaction. Events have positive unique tenant sequences and immutable
creation times. Runtime roles have no direct counter mutation grant.

The upgrade locks counters and events and verifies that each existing counter
has complete contiguous history ending at its retained `last_sequence`.
Missing or gapped history refuses the entire upgrade; no upgrade-time audit
facts are substituted. Backfill preserves the prior counter body and uses
the first event and maximum retained source creation time.

Existing allocators insert or advance counters before inserting the journal
entry. New-row clock defaults are provisional within that transaction. An
AFTER INSERT event trigger replaces them with retained source facts. A
deferred constraint trigger queries the final counter/journal state before
commit and requires exact clocks and the actual last source sequence. It
queries the final row rather than an earlier provisional row queued by the
allocator. Unbacked allocation or invented clocks cannot commit. The source
clock index and existing sequence key bound the point/extremum lookups.

Both trigger functions use fixed search paths, and PUBLIC execution is
revoked. No runtime table/function privilege, RLS policy, event envelope,
existing allocator or leased delivery capability changes. Immutable event
payload and first-publication protection remain. API/Worker startup requires
ledger `122_organization_metadata_stream_clocks`.

## Verification scope

The complete migration gate includes populated source history and a deliberately
unbacked counter. Refusal must preserve its full fingerprint and leave both
clock columns and ledger 122 absent. Removing only the unbacked fixture permits
upgrade and repeat. All prior counter fields and derived clocks are compared.
Rolled-back tests reject creation/update clock replacement and source-less
allocation when deferred constraints are forced, retain no-op clocks, and
verify a canonical metadata append advances the source clock while preserving
creation. All original migration gates remain; fixture-only versions advance
to 123–125.

The current restricted Organization metadata/lifecycle contracts exercise the
actual writers and existing claim/rollback/replay/security checks. An additional
database check compares every resulting stream's clocks and last sequence with
its retained source history. Full API/Worker required-ledger readiness is
included. These local compiled fixtures stage accounts/trusted application
context and earlier graph state; they are not HTTP/provider, full graph traversal
or immutable-image CI acceptance. The separate schema-121 complete browser
phase remains active while this schema-122 repair is verified.

The complete local migration gate and current restricted metadata/lifecycle
writer/readiness contracts pass. The populated historical body/clock comparison,
atomic missing-history refusal, no-op/tamper/unbacked-allocation checks and
canonical append checks pass. The locked Release build reports zero warnings
or errors, and all 179 integration/build metadata source checks pass without
skips. Independent verification confirms source/staging matches and cleanup of
the owned gate/contract containers and credential files; original services and
the active schema-121 browser phase are preserved. These reports remain private.

Other mutable-record gaps, including [Work event readiness history](work-event-clock-audit.md),
remain in the foundation audit. PRD-01 stays open at **34% estimated work
remaining** (planning estimate).
