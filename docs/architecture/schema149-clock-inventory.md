# Schema-149 timestamp inventory (PRD-01 FOUND-FR-009)

The [foundation acceptance map](prd-01-acceptance.md) requires creation/update
facts for mutable entities and a complete owning-writer/history audit. This
inventory refreshes its older schema-114 catalog baseline using all 149 ordered
migrations from `852ecb40ae116d311e13105db949719046a2fcd7`.

The [machine-readable snapshot](schema149-clock-catalog.json) includes every
public regular/partitioned base table, its conventional clock column types and
nullability, whether it has a `record_json` column, and installed non-internal
trigger/function names. It contains no relation rows, tenant/account values,
configuration values, credentials or provider data. Views and function bodies
require separate source review. Later migrations require a fresh capture.

## Observed physical columns

| Catalog category | Tables | Interpretation |
| --- | ---: | --- |
| Both `created_at` and `updated_at` exist and are non-null | 50 | Verify their owning writers, allowed transitions and historical semantics; column presence alone cannot prove those contracts. |
| At least one conventional clock column is absent | 57 | Includes the migration ledger, event/history/receipt facts and JSON-backed records; inspect each storage model before treating it as a defect or exemption. |
| Both columns exist and at least one is nullable | 7 | Trace legacy unknown clocks and every current writer; nullability alone does not establish missing new-write timestamps. |

These categories partition all **114** observed tables. Every physically present
conventional clock uses `timestamp with time zone`. The earlier 66-candidate
schema-114 count remains historical evidence; comparing the totals does not
prove that nine requirements were completed, because the schema and scope have
changed. Clock-column metadata for all 114 tables is retained in the snapshot.

## Source distinctions confirmed during this review

Organization configuration already has canonical clocks inside JSON:

- [The Application record](../../src/StrataAI.Application/Organizations/OrganizationConfiguration.cs)
  declares `CreatedAt` and `UpdatedAt`; the
  [PostgreSQL store](../../src/StrataAI.Infrastructure/Organizations/PostgresOrganizationConfigurationStore.cs)
  reads/writes that typed record through `record_json`.
- [Migration 137](../../db/migrations/137_organization_configuration.sql)
  parses both timestamps, requires finite ordered values, requires equality on
  the first revision and preserves the original `CreatedAt` on later revisions.
  Its history trigger requires the exact current source snapshot before
  publishing audit/event facts. The installed current/history guards appear in
  the catalog; conventional columns are not the only persisted clock storage.
- The history immutability trigger uses the
  [append-only audit guard](../../db/migrations/002_audit_runtime.sql).
  Missing conventional columns do not make that history a mutable entity.
  This source trace does not prove every receipt, provider or upgrade path.

The [attachment preview manifest guard](../../db/migrations/045_attachment_preview_intents.sql)
also rejects changed updates to `attachment_previews`, whose `created_at` is
present and whose `updated_at` is absent. Purge/deletion permissions and all
publication paths still need their owning-history audit; a trigger name or this
one refusal rule cannot establish complete attachment acceptance.

## Organization immutable facts and mutable owners — 2026-10-15

Three physical-column candidates have now been traced through their current
writers, source clocks, readers and installed mutation guards:

| Relation | Owning write and creation fact | Mutable counterpart |
| --- | --- | --- |
| `organization_board_events` | [Migration 073](../../db/migrations/073_organization_board_journal.sql) backfills canonical Work-event IDs and appends new entries through the source trigger. The composite tenant/event FK and [reader join](../../src/StrataAI.Infrastructure/WorkManagement/PostgresOrganizationBoardEventReader.cs) preserve the Work event's `created_at`; its publication readiness remains on that source. The history guard rejects every UPDATE/DELETE. | `organization_board_event_streams` owns the sequence counter and its separate creation/update clocks. A routing tuple does not become mutable when its source's readiness changes. |
| `organization_deletion_requests` | [The publisher](../../src/StrataAI.Infrastructure/Organizations/PostgresOrganizationDeletionJobPublisher.cs) inserts one accepted request with its existing `created_at`, progress and job in the owning transaction. Its existing-request branch verifies the original request/actor/version/progress/job instead of rewriting the request. [Migration 088](../../db/migrations/088_organization_deletion_progress.sql) rejects every UPDATE/DELETE before parent/membership checks. | Organization lifecycle and `organization_deletion_progress` advance independently. [The observation reader](../../src/StrataAI.Infrastructure/Organizations/PostgresOrganizationDeletionObservationReader.cs) derives completion from the matching Organization/progress/lifecycle source. [Migration 124](../../db/migrations/124_organization_deletion_progress_clocks.sql) governs progress audit clocks. |
| `organization_deletion_steps` | `apply_organization_deletion_page` in [migration 092](../../db/migrations/092_organization_deletion_pages.sql) inserts a completed page receipt with its original `completed_at` effect time, then advances progress and publishes the next job under the lease fence. Its history guard rejects every UPDATE/DELETE. | The progress cursor/phase/version and job lease/state change; the completed step receipt remains a fact. |

The [mandatory deletion-surface driver](../../scripts/ci/test-organization-deletion-surfaces.sh)
now checks no-op UPDATE, changed UPDATE and DELETE for all three relations after
each existing completed-workflow scenario. It requires nonempty targets, each
guard's exact SQLSTATE/message and unchanged complete target rows after every
refusal. Unexpected success or a different error fails the batch. Existing
visibility, retirement, audit, notification, source, retry and Worker assertions
remain active; no capacity stage or runtime permission is relaxed.

The **38.012-second** native verification creates one Organization, Board, List
and Card through real authenticated HTTP on compiled `d12a6f47` API/Worker,
using fresh PostgreSQL/all 149 migrations and restricted roles. The request
row fingerprint remains unchanged through the Owner's `COMPLETED` version-3
observation. Two Board routing rows join their finite canonical source clocks;
one request and four completed step facts remain. Nine independent refusal
probes each preserve a fingerprint of all **114 public base tables**. The exact
persisted Bash/SQL function then passes its same nine contracts with per-target
row comparisons and the whole public-table fingerprint unchanged afterward.

All **717 captured source/driver inputs and 71 binary files** match afterward.
The Release solution builds without warnings/errors, Bash syntax and all **285
CI coverage guards** pass. Owned containers, volume, network and **6,671 temporary
bytes** retire, restoring original Docker inventory. Fixture credentials stay in
child environments; its Compose file contains placeholders and its session stays
in memory. No existing local database/service, new image build/pull/import,
external provider or large-load test is used.

The first fixture attempt correctly withholds the Production Secure cookie over
plain loopback HTTP and stops before Organization creation; its 21.477-second
failure/cleanup is retained separately. Final verification limits its client
exception to the exact disposable loopback origin, refuses redirects and confirms
the cookie remains Secure. Application cookie flags and authorization are
unchanged. This controlled local IntegrationTest/compiled-runtime evidence does
not establish public HTTPS, strict verification/mail, the complete original
deletion-surface matrix or current immutable-image release acceptance. The new
registered assertions still require full current CI. Other candidates and
historical provenance remain in the full foundation audit.

## Retained identity receipt facts — 2026-10-16

Five candidates use insert-only application writers while their receipts remain
retained. Each records its actual database publication `created_at` and bounded
`expires_at`; token consumption also retains its accepted source `consumed_at`.
These global User receipts use forced subject RLS and a separate expired-only
Worker maintenance scope.

| Relation | Declaration and writer | Retained meaning |
| --- | --- | --- |
| `identity_revocation_replays` | [Migration 015](../../db/migrations/015_identity_revocation_replays.sql), [store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRevocationReplayStore.cs) | Original session/operation acknowledgment. The saver can remove its own expired same-key receipt before a fresh publication. |
| `identity_login_replays` | [Migration 017](../../db/migrations/017_identity_login_replays.sql), [store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityLoginReplayStore.cs) | Original credential-checked session coordinates and proof fingerprint. |
| `identity_registration_replays` | [Migration 018](../../db/migrations/018_identity_registration_replays.sql), [store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRegistrationReplayStore.cs) | Original registration/verification coordinates and source discriminator. |
| `identity_recovery_request_replays` | [Migration 019](../../db/migrations/019_identity_recovery_request_replays.sql), [store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRecoveryRequestReplayStore.cs) | Original reset/verification request's token coordinates, source and proof fingerprint. |
| `identity_token_consumption_replays` | [Migration 020](../../db/migrations/020_identity_token_consumption_replays.sql), [store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityTokenConsumptionReplayStore.cs) | Original accepted token-consumption fact and proof fingerprint. [The owning service](../../src/StrataAI.Application/Identity/IdentityService.cs) rereads the consumed token and saves its actual `UsedAt`; acknowledgment recovery requires that same source time. |

[Canonical runtime grants](../../db/provision-runtime-roles.sql) provide neither
role with an UPDATE path to these five receipts. The
[cleanup store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRetryCleanupStore.cs)
invokes bounded expired-only purge functions in its own transaction-local
`GLOBAL_IDENTITY_RETRY_CLEANUP` scope. Retention removes expired facts rather than
rewriting their clocks. By comparison,
[profile replay storage](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityProfileReplayStore.cs)
has an explicit upsert/update path; its current publication/replay/replacement
clocks are traced in the [mutable profile receipt review](#mutable-profile-receipt-clocks--2026-10-16).

The [mandatory runtime-role driver](../../scripts/ci/test-runtime-roles.sh) now
requires `has_any_column_privilege(...,'UPDATE')` to be false for both roles on
each receipt. A table-level UPDATE predicate stays false after a column-only
grant, so it cannot establish this invariant alone. In fresh PostgreSQL/all 149
migrations, the complete RLS prerequisite and updated role driver pass in a
**52.724-second** native fixture. Its expired-only visibility, protected proof
columns, rollback and 100-row cleanup boundaries remain active. Two retained
rows remain per receipt table. Ten normal runtime UPDATE attempts refuse with
SQLSTATE `42501`; all ten deliberate `UPDATE(created_at)` role/table grants are
rejected by the exact persisted new guard. Every refusal, grant probe and restored
guard preserves a fingerprint covering all **114 public base tables**.

All **192 captured database/store/driver/fixture inputs** match during that
invocation; owned resources retire and Docker inventory is restored. Its first
7.840-second attempt omits the preceding CI RLS Board seed and stops at that
precondition, with successful cleanup; the final run restores the unchanged
prerequisite. Separately, the complete updated
[native provisioning test](../../tests/identity_role_provision_test.py) passes in
**25.474 seconds**. It proves all ten column grants exist while table-level
UPDATE remains false, then verifies canonical reprovisioning clears them. The
provisioning SQL already implements that behavior and is unchanged. No owned
role fixture remains afterward. Neither invocation uses existing databases,
application hosts, providers, new images or large-load workloads.

Bash syntax and all **285 mandatory-CI coverage guards** pass. The stronger
privilege checks and provisioning regression run in their existing required
PostgreSQL job; no workflow gate, original fixture or acceptance assertion is
removed or deferred.

This classification relies on reviewed application writers and runtime grants;
these tables do not have a universal owner-level UPDATE/DELETE immutability
trigger. Privileged maintenance authority and intentional expiry deletion remain.
HTTP fresh-proof/recovery, all historical upgrade paths, other mutable records
and current immutable-image/full CI retain their own acceptance requirements.
No new `updated_at` value or historical time is invented. The physical catalog
counts stay unchanged; estimated PRD-01 work remaining stays **34%**.

## Mutable profile receipt clocks — 2026-10-16

`identity_profile_replays` is a mutable 24-hour acknowledgment cache, distinct
from the five insert-only retained facts above. [Migration 013](../../db/migrations/013_identity_profile_replays.sql)
requires non-null creation/update/expiry clocks from its original schema. The
[PostgreSQL replay store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityProfileReplayStore.cs)
publishes database-clock defaults, returns only live same-subject entries, and
replaces an expired row's fingerprint, result and all three clocks. The account
lock and [transactional service](../../src/StrataAI.Application/Identity/TransactionalIdentityService.cs)
serialize command/replay with the owning user mutation, audit, event and final
session admission. The [in-memory provider](../../src/StrataAI.Infrastructure/Identity/InMemoryIdentityProfileReplayStore.cs)
likewise removes expired entries before a new publication. This is deliberate
expired-key reuse, not permission to rewrite a live original acknowledgment.

| Clock owner | Meaning verified |
| --- | --- |
| `users` / embedded `UserProfile` | Original account `CreatedAt` survives profile edits; `UpdatedAt` comes from the accepted Application command's clock and the [profile writer](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityStore.cs) preserves creation while incrementing version. |
| `identity_profile_replays` | `created_at` and `updated_at` identify the current acknowledgment generation's publication; `expires_at` uses its 24-hour database-clock retention window. Matching replay preserves all fields and cannot slide expiry. Expired replacement publishes a new generation for the same key; it does not restamp the user's creation. |

The existing [profile expiry contract](../../tests/StrataAI.Persistence.Contracts/IdentityProfileExpiryContract.cs)
now brackets actual publication using database times and verifies finite ordered
receipt clocks and retention within that window. Both keyed/unkeyed commands
retain the original elapsed-session-expiry rollback and same-intent recovery,
source/audit attribution, unchanged session, replay/collision and state-fingerprint
assertions. A privately expired receipt then admits a version-3 replacement with
changed fingerprint/result, one retained receipt, exactly two sources/audits for
that account and an unchanged original user creation time. Replaying the new
generation preserves the complete snapshot, including clocks and expiry.

Native execution passes in **41.729 seconds** against fresh restricted PostgreSQL
17.11/all 149 migrations and current read-only compiled contracts in the cached
Linux runtime. The two accounts retain one receipt and three total profile
events/audits. A separate **40.802-second** negative probe installs a fixture-only
trigger that incorrectly preserves the expired generation's old clocks. The
new exact publication-window guard rejects it; its expected unhandled assertion
exits nonzero (**139**) and is retained privately. This is a deliberate negative
fixture, not an application success. Both runs match all **789 source inputs
and 41 binaries**, remove owned containers/network/tmpfs data and restore the
original Docker inventory. Credentials stay in child environments. No images
are built, pulled or imported; no release archives, providers, existing databases
or large-load tests are used.

The contract remains in the required default PostgreSQL persistence execution;
no workflow or original acceptance check is removed. Compilation has zero
warnings/errors and all **285** CI guards pass. Production SQL/schema/store
behavior is unchanged. This proves current owning-writer clocks and restricted
service transactions with an admitted fixture context; HTTP/managed deployment,
all historical producer/upgrade paths, other mutable writers and current
immutable/full CI retain their own acceptance. No unknown historical time is
backfilled. Catalog counts stay unchanged and PRD-01 stays open at **34% estimated
work remaining**; large-load testing remains deferred until after MVP.

## Personal Board preference and history clocks — 2026-10-16

`user_board_preferences` is mutable personal state; `board_star_events` is its
append-only private transition journal. They have different clock contracts:

| Record | Owning source and clock semantics |
| --- | --- |
| Current preference | [The PostgreSQL writer](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkManagementStore.cs) inserts both clocks from the accepted server timestamp. Later transitions preserve `created_at`, use `GREATEST` to preserve update-clock ordering, and increment the revision. Its conflict predicate prevents same-value changes from rewriting the row. Unstarring retains the same preference identity. |
| Legacy preference | [Migration070](../../db/migrations/070_board_star_revisions.sql) preserves unknown creation as null and the retained update time. [Migration071](../../db/migrations/071_board_star_private_journal.sql) assigns a stable identity without inventing a version1 event. Its guard preserves even a null creation value across later changes. |
| Private journal fact | The source trigger appends one event with the preference's accepted `updated_at`, stable entity/actor/Board identity and revision. The first new preference's event therefore shares its creation time; a legacy preference's first real post-upgrade transition publishes version2 without fabricating version1 history. UPDATE/DELETE are refused, including privileged no-op UPDATE. |

The [new upgrade contract](../../scripts/ci/test-board-star-legacy-clocks.sql)
runs inside the existing [complete migration driver](../../scripts/ci/test-migration-runner.sh)
after a real schema69 preference is upgraded through70/71. It checks a real
transition's database time window, preserved unknown creation, exact source-clock
history, no-op stability, refused creation invention/backwards update and
immutable history. Its transaction rolls back; the owning driver independently
checks the original preference fingerprint and empty journal before continuing
all later upgrades.

The existing [restricted HTTP fixture](../../scripts/ci/test-board-star-preferences.sh)
also checks persisted creation/update against database clock windows, exact
row stability on same-value intent and original-key replay, and the corresponding
version1/2 source clocks in history. Original actor isolation, stale/key conflict,
atomic rollback/recovery, private reads and revoked-admission checks remain.
These normal-clock cases do not establish every clock-rollback, historical or
other-writer path. No production store, schema or historical clock is changed.

The complete current migration driver exits **0**, including all149 migrations,
repeat/forward/serialized execution and intentional failure rollback. A private
counterfeit legacy-clock probe exits **3 at the exact new guard**:
`Legacy Board preference mutation manufactured a creation clock`. It combines
a before-update injector that actually changes the stored clock with an altered
private journal function; neither is committed or applied outside the disposable
upgrade database. The upgrade/negative sequence later stops before HTTP setup
because the private orchestration tries to recreate an already staged folder;
those completed phase outcomes are separate from that overall setup failure.

The original maintained HTTP script then exits **0** in a separate fresh
PostgreSQL17.11/schema149 fixture, with restricted runtime roles and current
read-only API/Worker binaries in cached runtimes. The fixture takes **67.873
seconds**; API identity/start time are unchanged. All **1,285 captured source
inputs, 261 staged SQL/shell sources and 71 runtime binaries** match. Embedded
runtime revision is `e6e1f4bb`; the script's historical success wording does not
certify current immutable release images. This fixture explicitly permits open
signup/unverified accounts and uses a controlled mail provider.

The initial SQL fixture expects a generic exception instead of the existing
guard's SQLSTATE23514; its catch is corrected to require that actual contract.
The first negative injector changes only an AFTER-trigger value and does not
persist the intended defect; it is corrected to introduce the stored defect
before the update. These fixture failures and the later duplicate-folder setup
failure remain retained. All four owned attempts restore original Docker
inventory, removing **5,426,798 temporary bytes** in total. No existing database,
application image build/pull/import, external provider or large-load test is used.
Bash syntax and mandatory CI coverage (four groups/seven executions/130 stages)
pass. The new SQL runs inside the existing required upgrade driver; the extended
HTTP script stays in its existing required command phase.

Current immutable-image/full CI, other writer/history paths and full issue
acceptance remain required. PRD-01 stays open at **34%**, PRD-04 at **15%
estimated work remaining**; these specific clock facts do not complete the MVP.

## Organization command receipt classification and capability checks — 2026-10-16

Five missing-update-column candidates use insert-only application stores and
retain their original acknowledgment after later entity changes. The
[owning service](../../src/StrataAI.Application/Organizations/OrganizationService.cs)
checks the original fingerprint/expiry; an expired key remains reserved instead
of being republished. Their `created_at DEFAULT clock_timestamp()` is the actual
receipt publication time, separate from creation/update clocks inside a returned
Organization snapshot. Each schema requires expiry after publication.

| Retained receipt | Schema | Owning store |
| --- | --- | --- |
| Metadata acknowledgment | [082](../../db/migrations/082_organization_metadata_replays.sql) | [Metadata store](../../src/StrataAI.Infrastructure/Organizations/OrganizationMetadataReplayStores.cs) |
| Departure acknowledgment | [083](../../db/migrations/083_organization_departure_replays.sql) | [Departure store](../../src/StrataAI.Infrastructure/Organizations/OrganizationDepartureReplayStores.cs) |
| Member-removal acknowledgment | [084](../../db/migrations/084_organization_removal_replays.sql) | [Removal store](../../src/StrataAI.Infrastructure/Organizations/OrganizationRemovalReplayStores.cs) |
| Creation acknowledgment | [086](../../db/migrations/086_organization_creation_replays.sql) | [Creation store](../../src/StrataAI.Infrastructure/Organizations/OrganizationCreationReplayStores.cs) |
| Deletion-request acknowledgment | [087](../../db/migrations/087_organization_deletion_replays.sql) | [Deletion store](../../src/StrataAI.Infrastructure/Organizations/OrganizationDeletionReplayStores.cs) |

The [canonical runtime provisioner](../../db/provision-runtime-roles.sql) grants
API SELECT/INSERT only and gives Worker no access to these receipts. There is
no application UPDATE, DELETE or upsert writer for them. Administrative expiry
edits in the [existing command fixture](../../scripts/ci/test-organization-commands.sh)
are outside that writer contract. The mutable Organization, membership,
lifecycle/progress and their histories retain their own clock requirements.

That fixture's five permission checks previously examined table grants only.
Twenty column-only grants (API UPDATE and Worker SELECT/INSERT/UPDATE across
five tables), plus five Worker DELETE grants, escape the original checks in a
**72.302-second** native baseline. The repaired checks use any-column predicates
for column-grantable privileges and also refuse Worker DELETE. The exact five
maintained Bash/SQL checks reject all **25/25** independent grant probes in
**71.271 seconds**, passing after each temporary grant is revoked.

Both runs use a fresh network-isolated PostgreSQL17/pgvector fixture with all
149 migrations and canonical runtime provisioning. They preserve the complete
row fingerprint of **114 public tables**, all **153 captured inputs** and the
original Docker inventory; owned container/tmpfs and temporary directory retire.
No business rows are needed for these capability checks. No application host,
existing database, new image build/pull/import, provider or large-load test is
used. Bash syntax and all 289 CI coverage checks pass.

Only the five existing required checks change; runtime grants, schemas,
application stores and original command/concurrency/expiry scenarios remain.
This source classification and isolated capability proof leave complete current
HTTP writer/history and immutable-image/full CI acceptance required. PRD-01
stays open at **34%**, PRD-03 at **8% estimated work remaining** (unchanged planning
estimates); no historical clock is manufactured and no issue is closed.

## Preview ledger capability checks — 2026-10-16

The attachment_previews relation records an immutable output declaration, rather
than the mutable progress of its owning job.
[Migration 045](../../db/migrations/045_attachment_preview_intents.sql)
inserts its created_at from clock_timestamp() in the lease-fenced declaration
transaction. An exact retry preserves the row; different output conflicts.
The manifest guard rejects changed updates. The separate
[publication receipt](../../db/migrations/046_attachment_preview_publication.sql)
uses published_at for the accepted publication effect, linked to its audit and
Work event. The Attachment and background job retain their separate mutable
lifecycle. These source distinctions do not establish every historical writer.

The provisioned API may read both ledgers; neither runtime may write them
directly, and the Worker reads through its fenced functions. The required
[runtime-role checks](../../scripts/ci/test-runtime-roles.sh) and
[upgrade assertion](../../scripts/ci/test-migration-runner.sh) previously used
table-only privilege predicates. PostgreSQL column grants can therefore violate
their intended contract without failing those assertions.

A fresh PostgreSQL 17/pgvector fixture with all 149 migrations confirms that
**12 column-grant probes escape the original checks**, while all **six table-grant
controls** are detected. The repaired assertions use has_any_column_privilege
for denied INSERT, UPDATE and Worker SELECT access; DELETE remains a table-level
check. They detect all **18/18** probes and controls. The positive API read
assertions and existing Worker function capabilities remain unchanged.

Each grant is independently revoked, the exact maintained assertions pass again,
and fingerprints of all **114 public tables** remain unchanged throughout the
probe phase. No business rows, application host, existing database, external
provider, image build/pull/import or large-load test is needed. Both probe
fixtures restore the original Docker inventory and remove their owned folders.

An initial probe stops on a nonexistent publication clock column; the completed
comparison uses a column present in both ledgers. A later full-driver transport
attempt stops on Windows checkout line endings before driver execution; its
disposable Linux copy requires normalized shell line endings. These setup
failures are retained separately from the completed grant probes.

The complete maintained migration driver passes. The runtime-role driver also
passes after its original RLS driver seeds the two Organizations/Boards required
by CI. An earlier standalone invocation omitted that prerequisite and stops at
its unchanged Board-count assertion; it is not counted as acceptance. The final
RLS/runtime invocation passes in **24.380 seconds**, preserves all **418 captured
database/driver inputs**, and restores Docker inventory after owned cleanup.
Its disposable shell copy uses Linux line endings; repository logic, assertions
and deadlines are unchanged. Bash syntax and all **289 CI coverage checks** pass.

This repair strengthens required CI admission checks. It does not change runtime
grants, migrations, application services or historical clocks. Current
immutable-image/full CI and the wider owning-writer
audit still govern acceptance. PRD-01 remains open at **34% estimated work
remaining**; PRD-14 remains open at **32%**. Large-load testing stays deferred.

## Nullable-clock review queue

| Relation | Nullable conventional clock fields | Owning migration/source to inspect |
| --- | --- | --- |
| `attachment_preview_sweeps` | Both | [Sweep audit clocks](../../db/migrations/129_attachment_sweep_audit_clocks.sql) explicitly preserve unknown legacy checkpoint times. |
| `attachment_scan_sweeps` | Both | The same migration preserves original cursors rather than using their candidate-order times as audit clocks. |
| `invitation_mail_intents` | `updated_at` | [Invitation mail clocks](../../db/migrations/131_invitation_mail_update_clocks.sql). |
| `invitation_recipient_authority_revisions` | Both | [Authority revision clocks](../../db/migrations/132_invitation_authority_revision_clocks.sql) documents why retained sources/effects cannot reconstruct first/last historical publication. |
| `user_board_preferences` | `created_at` | [Star revisions](../../db/migrations/070_board_star_revisions.sql) and [private journal guards](../../db/migrations/071_board_star_private_journal.sql). |
| `work_command_replays` | `updated_at` | [Replay update clocks](../../db/migrations/128_work_replay_update_clocks.sql). |
| `work_events` | `updated_at` | [Work-event update clocks](../../db/migrations/130_work_event_update_clocks.sql) and [the history/provenance audit](work-event-clock-audit.md). |

Do not fill unknown historical times with migration time, current time, cursor
ordering or unrelated source timestamps. Those values do not establish the
original mutation. The linked source contracts and executed upgrades govern
which later real transition can establish a known clock.

## Capture and acceptance boundary

The complete read-only capture takes **5.825 seconds** against a new,
network-isolated cached PostgreSQL/pgvector container with all 149 migrations.
It reads catalog metadata only. All **152 captured database/fixture inputs**
match afterward; owned container/tmpfs data and its empty private fixture
directory retire, restoring original Docker inventory. No existing local
database/service, new image build/pull/import, external provider or load test is
used. No business records are populated to infer historical clocks.

For a catalog-only inspection on a separately prepared disposable schema:

```sql
SELECT c.relname AS table_name, a.attname AS clock_column,
       NOT a.attnotnull AS nullable, format_type(a.atttypid,a.atttypmod) AS type
FROM pg_class c
JOIN pg_namespace n ON n.oid=c.relnamespace
LEFT JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0
  AND NOT a.attisdropped AND a.attname IN ('created_at','updated_at')
WHERE n.nspname='public' AND c.relkind IN ('r','p')
ORDER BY c.relname,a.attnum;
```

The inventory locates remaining work. Unreviewed candidates still need owning
writers, transitions, source projections, historical upgrade and refusal/rollback
evidence; reviewed current paths do not establish every historical producer.
Physical two-clock columns alone do not establish the remaining writers.
Current immutable-image/full CI and the rest of PRD-01 remain required.
Estimated PRD-01 work remaining stays **34%** (planning estimate); the issue
stays open and large load tests remain deferred until after the operational MVP.
