# Remaining mutable-clock candidates

This record supports the incomplete [foundation clock audit](prd-01-acceptance.md).
A read-only `information_schema` query against the live isolated schema-125
deletion-contract database finds **52 public base-table candidates** lacking
at least one physical `created_at` or `updated_at` column. The earlier schema-123
query found 53; deletion progress is no longer in this list after migration 124.
These counts are not defect counts or acceptance evidence. Immutable journals,
derived records and mutable operational records require writer/source analysis.
The private catalog snapshot contains schema metadata, not account rows.

Four remaining candidates have the following source trace. This is a selected
writer audit, not a classification of every remaining table.

| Candidate | Current writer/source | Finding and next requirement |
| --- | --- | --- |
| `notification_event_streams` | [Private journal migration 068](../../db/migrations/068_notification_private_journal.sql) allocates `(tenant_id, recipient_id)` sequence before appending a notification-created/read fact in the same transaction. Runtime callers receive journal/stream reads, with mutations behind the existing trigger capability. | The subsequent [clock repair](notification-stream-clocks.md) derives sequence-one/greatest retained source times and passes complete upgrade/refusal/source-producer checks. Full native producer/consumer, capacity and current release proof remain outstanding. |
| `attachment_preview_sweeps` | [Current lifecycle-aware preview backfill](../../db/migrations/051_attachment_lifecycle.sql) lazily inserts a tenant cursor, then advances it or resets both cursor fields to null at the end of a bounded scan. Source candidates are attachment creation times. | Cursor time is an attachment's time, not sweep creation or its last mutation. End-of-scan reset also loses the last candidate timestamp. Existing jobs are preserved by idempotency identity, but that does not reconstruct historical checkpoint clocks. A truthful legacy provenance/epoch strategy and full backfill recovery proof remain necessary. |
| `attachment_scan_sweeps` | [Current lifecycle-aware scan recovery](../../db/migrations/052_attachment_lifecycle_scan.sql) uses the same insert/advance/reset shape, scanning retained failed/exhausted scan jobs and admitting actual source/parent state before recovery. | A job's creation, lease expiry or attachment recovery time does not identify when the cursor was first inserted or last reset. Retained job/attachment clocks must not be renamed as sweep clocks. Historical treatment and unchanged scan/recovery/terminal fencing remain unresolved. |
| `invitation_recipient_authority_revisions` | [Metadata authority](../../db/migrations/105_invitation_recipient_authority.sql), [Board authority](../../db/migrations/107_invitation_recipient_board_authority.sql) and [issuer account authority](../../db/migrations/109_invitation_issuer_account_authority.sql) insert an effect and increment the same private recipient revision. | Retained source/effect identities establish why revisions changed, but effects have no per-recipient publication sequence or their own recorded clock. Trace all source kinds and first/increment provenance before selecting clock semantics. Preserve the email-scoped lookup policy and column-level reader grants; do not substitute the current migration clock. |

The existing [preview backfill contract](../../tests/StrataAI.Persistence.Contracts/AttachmentPreviewBackfillContract.cs)
uses an explicit admin fixture cursor reset to discover 36 legacy Clean files
across bounded pages. The [scan recovery contract](../../tests/StrataAI.Persistence.Contracts/AttachmentScanRecoveryContract.cs)
retains whole cursor-row fingerprints across refused recovery. Those source
tests locate the required behaviors; this audit does not re-execute them or
claim current runtime proof. [Work event delivery provenance](work-event-clock-audit.md)
also remains unresolved and is not waived by fixing other counters.

## Notification counter verification boundary

Repository-wide source inspection finds one counter allocator, in migration 068.
The notification trigger invokes it for creation and the first transition from
null `read_at`; replacement of an existing read time is refused. Counter increment
precedes the journal insert under the existing tenant advisory transaction lock.
A repair must validate the final transaction state rather than reject that
temporary counter/journal difference inside the allocator.

The [whole migration gate](../../scripts/ci/test-migration-runner.sh) already
checks journal backfill against notification creation/read facts, counter counts,
rollback of read transitions and unchanged replay. The
[runtime role gate](../../scripts/ci/test-runtime-roles.sh) requires API read-only
access to both tables, no Worker access, and no runtime execution of either
private journal function. New clock functions must retain that boundary.
The [source admission fixture](../../scripts/ci/notification-source-guard-fixture.sql)
compares complete stream rows when invalid producers are refused; the
[100,000-notification capacity scenario](../../scripts/ci/test-notification-capacity.sh)
also exercises the original journal count, cursor ordering and thirty first-read
facts. These are required regression sources, not freshly executed proof for an
unimplemented repair.

Sequence-one creation and greatest retained event time are prospective source
clocks. Before choosing them, verify every stream has complete positive contiguous
history, every event has its owning stream, and source clocks are finite. Existing
counter/journal/body identities must remain unchanged on upgrade, repeat and
refusal. Runtime append, earlier source times, no-op/replay and whole transaction
rollback need exact source-clock comparisons. The subsequent
[notification counter repair](notification-stream-clocks.md) records its own
implementation and executed evidence; this audit alone does not prove its
remaining runtime or release requirements.

No runtime clock, schema, grant or job behavior changes in this audit. The
current schema-125 build and running tests remain unchanged. Full mutable-clock
coverage, all foundation requirements and current build-once release acceptance
remain outstanding. PRD-01 stays open at **34% estimated work remaining**
(planning estimate).
