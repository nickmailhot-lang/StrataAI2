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
has an explicit upsert/update path and remains in the mutable-record audit.

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

The inventory locates remaining work. Every candidate still needs its owning
writers, transitions, source projections, historical upgrade and refusal/rollback
evidence; the 50 physical two-clock tables also need writer verification.
Current immutable-image/full CI and the rest of PRD-01 remain required.
Estimated PRD-01 work remaining stays **34%** (planning estimate); the issue
stays open and large load tests remain deferred until after the operational MVP.
