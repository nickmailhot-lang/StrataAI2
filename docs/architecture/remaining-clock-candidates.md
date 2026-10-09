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

## Complete schema-126 catalog navigation

A subsequent read-only query against the isolated, live schema-126 capacity
fixture finds **51** candidates. Notification counters now have both physical
columns. The private snapshot contains table/column metadata only; no account,
notification, invitation or other content rows are copied into this record.

Every candidate is listed below with its original declaration and retained
clock-bearing fields. These links are entry points into the complete ordered
migration history, not proof that the first declaration is the current writer.
Later replacements, runtime stores, capability grants, immutable guards and
administrative fixture transitions must also be traced. No table is exempted
from the foundation audit because its name suggests a journal or replay.

| Candidate | Original declaration | Retained clock fields |
| --- | --- | --- |
| `attachment_preview_publications` | [046_attachment_preview_publication.sql](../../db/migrations/046_attachment_preview_publication.sql) | `published_at` |
| `attachment_preview_sweeps` | [049_attachment_preview_backfill.sql](../../db/migrations/049_attachment_preview_backfill.sql) | `cursor_created_at` |
| `attachment_previews` | [045_attachment_preview_intents.sql](../../db/migrations/045_attachment_preview_intents.sql) | `created_at` |
| `attachment_scan_sweeps` | [050_attachment_scan_recovery.sql](../../db/migrations/050_attachment_scan_recovery.sql) | `cursor_created_at` |
| `audit_events` | [002_audit_runtime.sql](../../db/migrations/002_audit_runtime.sql) | `created_at` |
| `board_background_images` | [072_board_background_images.sql](../../db/migrations/072_board_background_images.sql) | `created_at` |
| `board_filter_interaction_replays` | [077_board_filter_interaction_replays.sql](../../db/migrations/077_board_filter_interaction_replays.sql) | `created_at`, `expires_at` |
| `board_star_events` | [071_board_star_private_journal.sql](../../db/migrations/071_board_star_private_journal.sql) | `created_at` |
| `card_assignment_notifications` | [031_card_assignment_notifications.sql](../../db/migrations/031_card_assignment_notifications.sql) | `created_at`, `read_at` |
| `comment_mention_recipients` | [058_comment_mention_snapshots.sql](../../db/migrations/058_comment_mention_snapshots.sql) | None |
| `comment_mention_snapshots` | [058_comment_mention_snapshots.sql](../../db/migrations/058_comment_mention_snapshots.sql) | `created_at` |
| `identity_events` | [012_identity_events.sql](../../db/migrations/012_identity_events.sql) | `created_at` |
| `identity_login_replays` | [017_identity_login_replays.sql](../../db/migrations/017_identity_login_replays.sql) | `created_at`, `expires_at` |
| `identity_recovery_request_replays` | [019_identity_recovery_request_replays.sql](../../db/migrations/019_identity_recovery_request_replays.sql) | `created_at`, `expires_at` |
| `identity_registration_replays` | [018_identity_registration_replays.sql](../../db/migrations/018_identity_registration_replays.sql) | `created_at`, `expires_at` |
| `identity_revocation_replays` | [015_identity_revocation_replays.sql](../../db/migrations/015_identity_revocation_replays.sql) | `created_at`, `expires_at` |
| `identity_token_consumption_replays` | [020_identity_token_consumption_replays.sql](../../db/migrations/020_identity_token_consumption_replays.sql) | `consumed_at`, `created_at`, `expires_at` |
| `invitation_creation_replays` | [022_invitation_creation_replays.sql](../../db/migrations/022_invitation_creation_replays.sql) | `created_at`, `expires_at` |
| `invitation_issuer_authority_effects` | [109_invitation_issuer_account_authority.sql](../../db/migrations/109_invitation_issuer_account_authority.sql) | None |
| `invitation_issuer_authority_proofs` | [109_invitation_issuer_account_authority.sql](../../db/migrations/109_invitation_issuer_account_authority.sql) | `changed_at` |
| `invitation_issuer_authority_sources` | [109_invitation_issuer_account_authority.sql](../../db/migrations/109_invitation_issuer_account_authority.sql) | `created_at` |
| `invitation_mail_intents` | [023_invitation_mail_intents.sql](../../db/migrations/023_invitation_mail_intents.sql) | `expires_at`, `finished_at`, `created_at` |
| `invitation_recipient_authority_effects` | [105_invitation_recipient_authority.sql](../../db/migrations/105_invitation_recipient_authority.sql) | None |
| `invitation_recipient_authority_revisions` | [105_invitation_recipient_authority.sql](../../db/migrations/105_invitation_recipient_authority.sql) | None |
| `invitation_recipient_authority_sources` | [107_invitation_recipient_board_authority.sql](../../db/migrations/107_invitation_recipient_board_authority.sql) | None |
| `invitation_recipient_events` | [102_invitation_recipient_events.sql](../../db/migrations/102_invitation_recipient_events.sql) | `created_at` |
| `invitation_recipient_organization_lifecycle_proofs` | [108_invitation_recipient_organization_lifecycle.sql](../../db/migrations/108_invitation_recipient_organization_lifecycle.sql) | `changed_at` |
| `invitation_recipient_organization_lifecycle_sources` | [108_invitation_recipient_organization_lifecycle.sql](../../db/migrations/108_invitation_recipient_organization_lifecycle.sql) | `created_at` |
| `invitation_recipient_proofs` | [102_invitation_recipient_events.sql](../../db/migrations/102_invitation_recipient_events.sql) | `created_at` |
| `mass_mention_reservations` | [061_mass_mention_quota.sql](../../db/migrations/061_mass_mention_quota.sql) | `source_created_at`, `reserved_at` |
| `mention_handle_reservations` | [056_mention_handles.sql](../../db/migrations/056_mention_handles.sql) | `created_at` |
| `navigation_interaction_events` | [078_navigation_interaction_sources.sql](../../db/migrations/078_navigation_interaction_sources.sql) | `created_at` |
| `navigation_interaction_replays` | [080_navigation_interaction_replays.sql](../../db/migrations/080_navigation_interaction_replays.sql) | `created_at`, `expires_at` |
| `notification_events` | [068_notification_private_journal.sql](../../db/migrations/068_notification_private_journal.sql) | `created_at` |
| `organization_board_events` | [073_organization_board_journal.sql](../../db/migrations/073_organization_board_journal.sql) | None |
| `organization_creation_replays` | [086_organization_creation_replays.sql](../../db/migrations/086_organization_creation_replays.sql) | `created_at`, `expires_at` |
| `organization_deletion_replays` | [087_organization_deletion_replays.sql](../../db/migrations/087_organization_deletion_replays.sql) | `created_at`, `expires_at` |
| `organization_deletion_requests` | [088_organization_deletion_progress.sql](../../db/migrations/088_organization_deletion_progress.sql) | `created_at` |
| `organization_deletion_steps` | [092_organization_deletion_pages.sql](../../db/migrations/092_organization_deletion_pages.sql) | `completed_at` |
| `organization_departure_replays` | [083_organization_departure_replays.sql](../../db/migrations/083_organization_departure_replays.sql) | `created_at`, `expires_at` |
| `organization_invitation_acceptances` | [101_organization_invitation_acceptance_events.sql](../../db/migrations/101_organization_invitation_acceptance_events.sql) | `accepted_at`, `updated_at` |
| `organization_invitation_creations` | [099_organization_member_invitation_events.sql](../../db/migrations/099_organization_member_invitation_events.sql) | `created_at` |
| `organization_invitation_revocations` | [100_organization_invitation_revocation_events.sql](../../db/migrations/100_organization_invitation_revocation_events.sql) | `revoked_at`, `updated_at` |
| `organization_membership_activations` | [097_organization_member_addition_events.sql](../../db/migrations/097_organization_member_addition_events.sql) | `activated_at` |
| `organization_membership_removals` | [098_organization_member_removal_events.sql](../../db/migrations/098_organization_member_removal_events.sql) | `removed_at` |
| `organization_metadata_replays` | [082_organization_metadata_replays.sql](../../db/migrations/082_organization_metadata_replays.sql) | `created_at`, `expires_at` |
| `organization_removal_replays` | [084_organization_removal_replays.sql](../../db/migrations/084_organization_removal_replays.sql) | `created_at`, `expires_at` |
| `schema_migrations` | [001_foundation.sql](../../db/migrations/001_foundation.sql) | `applied_at` |
| `search_interaction_events` | [076_search_interaction_sources.sql](../../db/migrations/076_search_interaction_sources.sql) | `created_at` |
| `work_command_replays` | [010_work_command_replays.sql](../../db/migrations/010_work_command_replays.sql) | `created_at`, `expires_at` |
| `work_events` | [011_work_events.sql](../../db/migrations/011_work_events.sql) | `created_at`, `ready_at` |

### Additional writer findings

`invitation_mail_intents` has a mutable state, version, receipt, finish time and
error code. [Its current runtime publisher](../../src/StrataAI.Infrastructure/Onboarding/PostgresInvitationMailPublisher.cs)
inserts the snapshot selected from a freshly admitted invitation; the original
private `finish_invitation_mail` capability locks the intent and job, validates
the tenant/actor/metadata and current Worker lease after waits, then updates only
a pending row to a terminal state with a database finish time and version advance.
The capability admits SENT, CANCELLED and FAILED. API table-state updates and
Worker direct recipient updates are denied by the
[original mail-scope regression](../../scripts/ci/test-invitation-mail-scope.sh).

That regression also performs admitted administrator changes to the pending
Board-target snapshot, including changing and restoring the target role. Such
updates do not record a mutation time. `COALESCE(finished_at,created_at)` therefore
covers runtime terminal-state publication, but does not prove the last mutation
clock of every historically admitted row. A repair must explicitly handle this
legacy distinction, preserve all target-scope/refusal/lease/replay tests, and not
replace missing mutation provenance with the migration clock. This source trace
is not a newly executed mail-delivery or whole-schema acceptance result.

The recipient-authority revision has an additional counting boundary. Migration
105 increments once after each new tenant/source/email effect; 107 retains that
rule for the expanded source view. The current replacement in 109 first records
the tenant effect, then deduplicates User-source effects globally by event/email
in `invitation_issuer_authority_effects`. Only the first global User effect
increments the private recipient revision, even if that issuer invited the same
address into multiple Organizations. Thus counting every tenant effect would
inflate the expected revision. A retained-history check must count non-User
tenant/source effects plus deduplicated global User effects and independently
validate their source/actor/email relationships. Neither effect table records a
per-recipient publication sequence; source event time alone is not proof of
which effect first created the counter. This blocks an unqualified sequence-one
clock reconstruction, not further work on other requirements.

The full catalog, selected writer traces and missing legacy provenance remain
separate from complete mutable-record classification and runtime acceptance.
PRD-01 remains open at **34% estimated work remaining** (planning estimate).

### Notification journal classification

`notification_events` is now protected as an append-only fact rather than
classified from its name. The [forward history guard](notification-event-history.md)
validates every retained parent fact and counter clock before installation,
rejects journal edits/deletion, preserves original producer inserts and passes
the complete upgrade/refusal and role gates. Its existing `created_at` is the
owning creation/first-read time. The absent physical `updated_at` does not
represent an untracked mutable state after this guard. Native schema-127 and
current release proof remain pending; this classification does not classify
the other 50 catalog candidates or establish complete foundation acceptance.

### Four additional history-table writer classifications

A read-only schema-127 trigger catalog records 49 noninternal trigger rows
across 29 candidates. Function names alone do not establish immutability.
The following four classifications additionally trace the actual function
bodies, every migration reference, application references and administrative
fixture transitions. All four guards unconditionally raise SQLSTATE `23514`
on row UPDATE or DELETE; none provides a runtime mutation branch.

| Candidate | Writer and source clock | Classification and remaining boundary |
| --- | --- | --- |
| `board_star_events` | [Migration 071](../../db/migrations/071_board_star_private_journal.sql) appends only actual preference transitions, using the preference's admitted `updated_at`. Unchanged preferences return without an event; legacy preferences acquire no invented events. | Append-only transition facts with a source `created_at`. The original [HTTP/SQL star gate](../../scripts/ci/test-board-star-preferences.sh) covers a refused administrative no-op update and runtime write denial; it was located, not re-executed in this classification run. |
| `organization_board_events` | [Migration 073](../../db/migrations/073_organization_board_journal.sql) appends `(tenant_id,sequence,event_id)` pointers after actual canonical Board source insertion. Its [reader](../../src/StrataAI.Infrastructure/WorkManagement/PostgresOrganizationBoardEventReader.cs) joins the source event's `created_at` and delivery readiness. | Immutable journal pointers, rather than independently mutable entities with missing clocks. The [ordering fixture](../../scripts/ci/test-organization-board-journal-order.sh) explicitly disables the history trigger for disposable administrative cleanup, then restores it. This exception is not runtime authority. `work_events.ready_at` and its historical delivery-clock gap remain a separate unresolved candidate. |
| `search_interaction_events` | [Current migration-085 capability](../../db/migrations/085_interaction_actor_lock_order.sql) validates actor/target and exact original source identity, then inserts once using the admitted finite `p_created`. Replays return the original event without editing it. | Append-only actor-owned facts carrying `created_at`. Search stream counters and replay rows are separate candidates; this classification does not exempt their mutation clocks. |
| `navigation_interaction_events` | The same [current capability migration](../../db/migrations/085_interaction_actor_lock_order.sql) retains original-event recovery, current actor/target admission and finite source time before one insertion. | Append-only actor-owned facts carrying `created_at`. Navigation replay rows and mutable navigation preferences require their own writer analysis. |

On 2026-10-09, a fresh isolated PostgreSQL/pgvector instance applies all 127
ordered migrations and passes the complete unchanged original
[Organization journal SQL](../../scripts/ci/test-organization-board-journal.sql),
[search source SQL](../../scripts/ci/test-search-interaction-sources.sql) and
[navigation source SQL](../../scripts/ci/test-navigation-interaction-sources.sql)
gates. These exercise their original source admission, isolation, replay and
rollback scopes; the Organization/search gates also refuse administrative
history updates. The navigation gate checks runtime update denial, not every
administrative mutation. The scripts remain byte-identical after newline
normalization. The owned container and environment file are removed and
independently confirmed absent. No production schema, grant or clock changes.

Together with `notification_events`, these are five source-classified immutable
history candidates from the 51-table physical-column list. The other **46**
remain subject to complete classification; selected writer traces above are
not complete classifications. The schema-127 notification producer/consumer
phase now passes all nine original cases and its whole-counter oracle, as
recorded in [notification verification](notification-stream-clocks.md).
Full mutable-clock coverage, historical provenance, capacity and current
immutable build-once release acceptance remain outstanding. PRD-01 remains
open at **34% estimated work remaining** (planning estimate).
