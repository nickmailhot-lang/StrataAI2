# Invitation mail update clocks and delivery boundaries

[PRD-01 FOUND-FR-009](prd-01-acceptance.md) requires creation and update
timestamps for mutable entities. [Migration 131](../../db/migrations/131_invitation_mail_update_clocks.sql)
adds a managed `updated_at` to `invitation_mail_intents` without changing
delivery capability signatures, runtime grants, lease checks or provider
receipt semantics.

## Recorded times

New intents initialize `updated_at` to their finite `created_at`, ignoring a
caller-supplied update clock. The creation time becomes immutable. Every
admitted change to another stored field captures the greatest of database
time, creation time and the prior known update time. Exact no-ops and edits
only to the managed update clock preserve its prior value. Refused payload
or creation changes roll back their attempted clock effects.

This includes the original lease-bound pending-to-terminal transition and
admitted administrative pending payload edits. It does not add Worker or API
permission to edit those payloads. The separate `finished_at`, state, receipt,
safe error and increasing version retain their original meaning. The trigger
uses a fixed search path and has no PUBLIC execution capability.

## Legacy provenance

The upgrade preserves every original field of pending, completed and
previously edited intents. Their historical `updated_at` stays NULL, meaning
unknown. `COALESCE(finished_at,created_at)` does not establish the time of every
historically admitted payload edit. Neither migration time nor job expiry
can recover that missing fact. A subsequent actual change records that new
update; a no-op leaves unknown provenance unknown.

Nonfinite legacy creation times refuse the migration before schema changes.
The migration runner verifies rollback, restores the original fixture value,
then verifies forward application and repeat application through version 131.
No legacy receipt is rewritten, reissued or treated as pending to manufacture
an audit timestamp. Legacy provenance remains within the open acceptance scope.

## Executed local checks

- Locked solution build: zero warnings and errors.
- Ten complete PostgreSQL gates in CI prerequisite order: tenant catalog,
  full migration runner through 131, RLS, original activity attribution,
  runtime-role provisioning/checks, mail clocks, Work event clocks, sweep
  clocks, receipt clocks and the original mail scope fixture.
- A fresh four-gate companion reruns RLS, runtime roles, mail clocks and the
  corrected full mail scope fixture against current source.
- Original compiled readiness mode individually refuses/restores every one
  of 131 required ledger entries for both API and Worker.

[The mail clock gate](../../scripts/ci/test-invitation-mail-update-clocks.sql)
uses the actual restricted Worker finish capability with a live job lease.
It checks forged insertion clocks, a refused wrong lease with exact unchanged
rows, successful completion clocks and retained identity, duplicate/no-op
behavior, creation tamper refusal and atomic rollback after an invalid payload.
[Upgrade fixtures](../../scripts/ci/mail-clocks-before-upgrade.sql) retain
pending, terminal and administratively edited history without fabricated clocks.

[The original mail scope fixture](../../scripts/ci/test-invitation-mail-scope.sh)
retains tenant/actor/worker/lease binding, canonical eligibility, account
isolation, Portal separation and the original lease-expiry-under-lock checks.
Its unbound-job insertion now uses explicit columns and a distinct valid
invitation, so an unrelated column-shape or duplicate-invitation error cannot
mask the job-binding check. The case must fail on the named job/tenant/issuer
foreign key and leave the original intent count unchanged.

Private reports are outside the checkout:
`invitation-mail-clocks-schema131-ci-order-native-20261009`,
`invitation-mail-clock-scope-current-native-20261009` and
`invitation-mail-schema131-readiness-native-20261009`. Their owned containers
and credential environments are cleaned up. The first ten-gate report predates
the unbound-job fixture correction; the separate current-source companion
proves that correction. These are local compiled/database checks, not external
mail-provider delivery or immutable-image release acceptance.

## Remaining verification

The original full schema-130 persistence executable timed out preparing the
unchanged 100,002-Card deletion candidate fixture. CI run `37998254598`
independently reproduces that timeout. Full schema-131 persistence is running
with private slow nested-query plan logging to investigate it. No case retry,
filter, cardinality, deadline, assertion or page budget is changed; diagnostic
logging itself is not a production configuration or latency certification.
The full failure and current CI evidence remain open obligations.

[Recipient-authority revision counters](invitation-authority-revision-clocks.md)
now have prospective clocks in migration 132. Legacy provenance/full acceptance
remain unresolved for all six classified mutable candidates. PRD-01 stays open at **34% estimated work
remaining** (planning estimate).


The schema-131 diagnostic default invocation subsequently passes and removes
its owned resources. Its instrumented local result does not erase the earlier
local/CI timeout or certify schema 132. The isolated Card route-batching
experiment and its remaining complete deletion verification are recorded in
[source test results](source-test-results.md).
