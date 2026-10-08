# Identity lifecycle audit clocks

FOUND-FR-009 and PRD-02's data requirements require audit clocks on mutable
records. Sessions, password-reset tokens and email-verification tokens previously
had `created_at` and lifecycle-specific clocks but no `updated_at` field.

[Migration 114](../../db/migrations/114_identity_lifecycle_clocks.sql) adds a
non-null update clock to those three tables. It backfills the latest known
creation, revocation, consumption or session-seen clock. It preserves the original
rows and does not substitute the migration time for a historical mutation.
An insertion default supports existing administrative fixtures. Owning product
writers explicitly persist the accepted creation or lifecycle time.

Session and token records initialize `UpdatedAt` from `CreatedAt`. PostgreSQL and
Demo writers advance it on actual token consumption, session revocation, password
reset revocation and account deactivation. Repeated revocation preserves the first
revocation and update clock; refused repeated consumption changes neither. These
clocks remain inside the owning transaction, including rollback. No new public
credential/session fields, permission grants, token format or audit events are
introduced. Future last-seen writers must also advance the update clock; there is
currently no product last-seen writer.

The required API/Worker migration ledger includes 114. Both runtimes refuse an
incomplete ledger. The existing migration-runner gate adds a forward upgrade and
repeat execution with [nine complete historical rows](../../scripts/ci/identity-lifecycle-clocks-before-upgrade.sql),
then [checks their preserved state and known clocks](../../scripts/ci/identity-lifecycle-clocks-after-upgrade.sql).
Its serialization, failed-migration and unrecorded-migration fixtures retain their
existing semantics under subsequent fixture-only numbers.

The ordinary PostgreSQL contract suite includes the
[restricted lifecycle clock contract](../../tests/StrataAI.Persistence.Contracts/IdentityLifecycleClockContract.cs).
It checks creation, verification, reset, first/repeated revocation, deactivation,
refused repeats and complete account/session/token rollback through the real
restricted store and owning token transaction. Its focused invocation is
`--identity-lifecycle-clocks-only`; this does not replace the full suite.

On 2026-10-08, an isolated schema-113 baseline confirms all three columns absent.
The actual forward upgrade preserves all nine complete historical rows. The
configured restricted writer contract and required-ledger refusal/restoration
checks pass against PostgreSQL 17/pgvector through schema 114. Seventeen API-host
token-consumption rollback/replay, revocation retry, current-session recovery and
legacy-hash cases pass; the strict locked Release build has zero warnings/errors.
The first direct-store harness stops at its omitted required private retry-key
configuration; it is retained separately, rather than claimed as a product failure
or passing execution. Both owned database containers are removed, with original
services and volumes preserved.

This proves the declared upgrade and store/transaction scope. It does not prove
current retained-image HTTP/browser/Worker delivery or the complete migration
runner and full CI. The new source gates must pass for the committed revision.
This also does not certify audit-clock coverage for every other mutable record;
for example, invitation lifecycle clocks still require their own writer audit.
PRD-01 remains open at **34% estimated work remaining** and PRD-02 at **16%**
(planning estimates).
