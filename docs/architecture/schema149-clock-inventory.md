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
