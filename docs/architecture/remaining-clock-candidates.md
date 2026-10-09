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

### Invitation authority history writer classifications

Eight additional candidates have immutable row payloads after insertion. The
schema-127 catalog confirms their BEFORE UPDATE/DELETE triggers, and the writer
audit traces current migration replacements and application/fixture references.
The shared `protect_invitation_authority_history` has a completion branch only
for `invitation_recipient_authority_pages`; none of these eight tables uses
that branch. The recipient event guard unconditionally refuses updates/deletes.

| Candidate | Current insertion and retained provenance | Boundary |
| --- | --- | --- |
| `invitation_recipient_events` | [Migration 104](../../db/migrations/104_invitation_recipient_retained_board_admin.sql) admits the original invitation proof, actor and target grant before appending once with `proof.created_at`. | Immutable recipient journal facts; streams remain separate mutable counters. |
| `invitation_recipient_authority_sources` | [Migration 113](../../db/migrations/113_invitation_recipient_membership_authority.sql) inserts an exact pointer to metadata, Work, Organization lifecycle or issuer sources. Its current view joins the corresponding source identity and `created_at`. | Immutable source pointers. A joined source time is not claimed as the later pointer's publication time. |
| `invitation_recipient_authority_effects` | [Migration 109](../../db/migrations/109_invitation_issuer_account_authority.sql) inserts each tenant/source/email effect once, before its admitted revision effect. | Immutable deduplication facts, with source identities but no recorded effect-publication time. This does not repair the recipient revision counter's missing clock provenance. |
| `invitation_issuer_authority_proofs` | The same migration captures actual User deactivation with its admitted version, `updated_at` as `changed_at`, and owning transaction. | Immutable transition proofs; ordinary `identity_events` are not classified wholesale by the conditional deactivation guard. |
| `invitation_issuer_authority_sources` | The same migration admits the actual identity event against its captured proof and inserts `created_at` with original actor/version/correlation identity. | Immutable canonical source facts, with no inferred legacy events. |
| `invitation_issuer_authority_effects` | The same publisher inserts a global event/email effect only once across Organizations. | Immutable deduplication facts; their absence of publication sequence/time remains relevant to revision-clock reconstruction. |
| `invitation_recipient_organization_lifecycle_proofs` | [Migration 108](../../db/migrations/108_invitation_recipient_organization_lifecycle.sql) captures actual ACTIVE→DELETING and DELETING→DELETED transitions with version, admitted `updated_at` and owning transaction. | Immutable transition proofs, separate from mutable deletion progress. |
| `invitation_recipient_organization_lifecycle_sources` | The same migration validates the request audit or terminal lifecycle event against its exact transition proof before inserting the canonical source. | Immutable source facts with finite `created_at`, not synthetic attribution for historical Organization states. |

The first fresh schema-127 SQL verification attempt stops at the original
recipient-event gate's runtime-role prerequisite because the private helper
omitted role provisioning. That failed attempt and private diagnostics remain
retained; it is not a product refusal or a passing gate. Its owned resources
are removed. A subsequent fresh isolated PostgreSQL/pgvector invocation applies
all 127 migrations, provisions the standard restricted API/Worker roles and
passes all four unchanged original gates:
[recipient events](../../scripts/ci/test-invitation-recipient-events.sql),
[recipient authority](../../scripts/ci/test-invitation-recipient-authority.sql),
[Board authority](../../scripts/ci/test-invitation-recipient-board-authority.sql)
and [authority discovery](../../scripts/ci/test-invitation-recipient-authority-discovery.sql).
Staged scripts match source after newline normalization. Owned containers and
environment files are independently absent. These gates retain their complete
source/authority/RLS/replay/rollback scopes; they are not a new execution of
the separate issuer-account or Organization-lifecycle persistence contracts,
nor native browser/Worker transport or immutable-image release acceptance.

This brings source classification to **13 of the 51** catalog candidates;
**38** still require complete classification. `invitation_recipient_proofs`
remains separate: migration 103 permits nested deletion of unpublished proofs
during owning invitation cleanup, so its guard is not unconditional. The
mutable recipient-authority revision's missing publication provenance, sweep
checkpoints and Work-event delivery clocks remain unresolved. No schema,
grant, clock or runtime behavior changes. PRD-01 stays open at **34% estimated
work remaining** (planning estimate).

### Reconciled audit, image ownership and notification state

Three further candidates have writer classifications already supported by
their source contracts. This reconciliation counts those implementations in
the catalog audit without claiming that physical column names define defects.

| Candidate | Writer classification and clock rule | Verification boundary |
| --- | --- | --- |
| `audit_events` | [Migration 002](../../db/migrations/002_audit_runtime.sql) creates append-only facts with `created_at` and unconditional BEFORE UPDATE/DELETE refusal. API/Organization/Work/identity publishers and narrow SQL capabilities insert facts in the owning transaction; fault-injection fixtures refuse insertion rather than edit retained history. | The complete original audit append-only gate now passes on fresh schema 127, together with its RLS prerequisite and the runtime-role gate. This verifies update/delete refusal and role isolation, not every application's producer transaction. |
| `board_background_images` | The existing [ownership audit](board-background-clock-audit.md) traces image-selection/copy insertion with `created_at`. Migration 072 refuses every update/delete; later image selection updates the canonical Board, preserving ownership facts. The schema-127 trigger definition agrees with that classification. | Earlier source/installed-metadata evidence remains scoped as recorded. This reconciliation does not claim a new execution of all publication/tamper/storage contracts. The complete current Board browser group is still running. |
| `card_assignment_notifications` | This is mutable read state with an [authoritative derived update clock](notification-audit-clocks.md), rather than an immutable event: unread `updatedAt=createdAt`, first-read `updatedAt=readAt`. The persisted first-read fact is retained through original-key/natural retries; replacing an existing first-read time is refused. | Full schema-127 API, nine-case native notification producer/consumer, twelve-case watch/order and full-count capacity evidence are recorded in [notification verification](notification-stream-clocks.md) and [source results](source-test-results.md). These local scopes do not establish current immutable-image/full PRD acceptance. |

The fresh schema-127 audit invocation applies all 127 ordered migrations,
provisions standard restricted API/Worker roles and passes the unchanged
original [RLS](../../scripts/ci/test-rls.sh),
[append-only audit](../../scripts/ci/test-audit-append-only.sh) and
[runtime-role](../../scripts/ci/test-runtime-roles.sh) scripts. Staged sources
match after newline normalization; the owned container and environment files
are independently absent. No schema, privilege or runtime code changes.

The reconciled total is **16 of 51** source-classified candidates: **15 immutable
history/ownership tables and one mutable notification table with a derived
clock**. The other **35** still require complete classification. This does not
waive the legacy sweep, Work delivery or recipient-revision provenance gaps.
The local full-count notification capacity run is now terminal and passing
for its recorded seeded-consumer scope. Full mutable-record coverage and
current immutable build-once release acceptance remain outstanding. PRD-01
stays open at **34% estimated work remaining** (planning estimate).

### Preview publication and manifest boundary

`attachment_preview_publications` is an immutable publication receipt. Its
[migration-046 guard](../../db/migrations/046_attachment_preview_publication.sql)
rejects every update/delete, including no-op updates. Insertion must match the
original preview manifest/job and the same transaction's audit and Work event:
actor, attachment/Card/Board identities, admitted versions, `published_at`,
source creation times and ready-state publication evidence. The current
`finish_attachment_preview` capability inserts the receipt after admission;
replay returns the retained publication instead of editing it. Repository-wide
writer inspection finds this producer and explicit refusal fixtures, with no
runtime update/delete writer or administrative history-guard bypass.

Read-only installed-metadata verification against the isolated live full
persistence database on 2026-10-09 confirms all 127 ledger entries, an enabled
BEFORE INSERT/UPDATE/DELETE row guard, forced RLS, API UPDATE/DELETE denial and
Worker INSERT/UPDATE/DELETE denial. No publication rows are read or changed by
that probe. This adds one source-classified immutable receipt: **17/51**
candidates classified (**16 immutable, one mutable derived-clock table**),
with **34** still requiring complete classification. It does not substitute
for executing the original tamper/publication/replay contracts.

`attachment_previews` remains separately bounded. Its
[manifest guard](../../db/migrations/045_attachment_preview_intents.sql) refuses
changed fields on UPDATE but accepts exact no-ops, and has no unconditional
DELETE branch. Runtime writers are private insertion capabilities and runtime
table writes are denied; a published receipt's FK prevents deleting its owning
manifest. Those facts do not establish identical deletion semantics for an
unpublished manifest. It is not counted as a completely classified append-only
table in the total above, and its `created_at` is not renamed as a job's last
lease/recovery mutation clock.

A complete **unfiltered** schema-127 persistence executable is now running
against its own fresh PostgreSQL/pgvector instance with restricted API/Worker
logins and the original compiled contract payload. It retains every original
preview, lifecycle, concurrency and scale contract, with no `--only` arguments,
test filters, exclusions, retries or adjusted budgets. Its terminal result and
cleanup remain required. The staging comparison initially flagged differing
Windows newline encodings; independently normalizing both sides confirms all
127 migration sources match current source. The existing 14-file/32-case Board
browser invocation also remains live; its recorded lifecycle navigation failure
must be retained when the whole report becomes terminal. Current immutable
build-once release acceptance remains outstanding. PRD-01 remains open at
**34% estimated work remaining** (planning estimate).

### Mention revision history and privileged cleanup

Two further candidates are immutable **runtime revision metadata**, with
privileged cleanup semantics distinct from unconditional history guards.
The repository-wide writer audit finds insertion in the owning comment
transaction and explicit administrative cleanup in disposable persistence
contracts; it finds no runtime UPDATE/DELETE producer for either table.

| Candidate | Writer and clock provenance | Mutation and retention boundary |
| --- | --- | --- |
| `comment_mention_snapshots` | [Comment producer](../../src/StrataAI.Application/WorkManagement/CardCommentService.cs) inserts a snapshot for each actual create/edit/soft-delete revision, using the same database-normalized timestamp as that revision. [Migration 058](../../db/migrations/058_comment_mention_snapshots.sql) requires the current comment version and exact `updated_at=created_at`; a deleted revision must have zero recipients. | Changed payloads are refused, exact no-op UPDATE is accepted, and the restricted API has only SELECT/INSERT. There is no unconditional administrative DELETE guard. A later revision inserts a new snapshot rather than updating the old one. |
| `comment_mention_recipients` | [Snapshot store](../../src/StrataAI.Infrastructure/WorkManagement/PostgresCommentMentionSnapshotStore.cs) inserts the exact stable recipient set after its snapshot header, in the same owning transaction. The composite FK identifies the original revision/header and its source `created_at`; recipients are not independently mutable timestamped entities. | Changed recipient identities are refused; exact no-op UPDATE is accepted. Deferred cardinality prevents a partial recipient set from committing. Runtime UPDATE/DELETE are denied; privileged cleanup can delete recipients and headers together. |

The store validates exact replay against the retained timestamp, count and
sorted recipient identities. Editing to an empty set preserves previous
revision identities. [Migration 060](../../db/migrations/060_mass_mention_recipient_history.sql)
removes the username-only recipient-count cap for confirmed groups without
changing these clock, revision or cardinality rules. The Worker has no table
access. Administrative fixture cleanup is not evidence of a runtime mutable
entity, and these tables are not classified as having unconditional deletion
refusal.

On 2026-10-09 the complete unchanged original
[mention snapshot SQL gate](../../scripts/ci/test-comment-mention-snapshots.sql)
passes on fresh schema 127. The first companion runtime-role invocation fails
because the private helper omitted the original RLS seed prerequisite; that
failed attempt remains retained and is not a product failure or a whole-gate
pass. A fresh invocation applies all 127 migrations and standard restricted
roles, then passes the complete original [RLS gate](../../scripts/ci/test-rls.sh),
mention snapshot gate and [runtime-role gate](../../scripts/ci/test-runtime-roles.sh)
in that order. Assertions, fixture scope and scripts remain unchanged; staged
sources match after normalizing both sides' newline encoding. Both attempts'
owned containers and environment files are independently confirmed absent.

The source-classified total is now **19/51**: **16 unconditional immutable
history/ownership candidates, two runtime-immutable revision metadata
candidates with privileged cleanup, and one mutable derived-clock table**.
The remaining **32** still require complete writer/retention/clock
classification. This SQL scope does not replace complete persistence,
native producer/consumer, capacity or immutable-image release verification.
The unfiltered persistence executable and fresh full 32-case Board browser
run are still active; current main CI is queued. No schema, grants or runtime
behavior change. PRD-01 remains open at **34% estimated work remaining**
(planning estimate).

### Retry receipt payloads, expiry and reserved keys

Thirteen additional candidates have no runtime payload UPDATE writer. Their
retention rules are part of the classification: absence of `updated_at` does
not turn a receipt's `expires_at` into a last-mutation timestamp, and deletion
after expiry is not described as unconditional append-only history.

| Candidate | Owning insertion and receipt clock | Retention boundary |
| --- | --- | --- |
| `identity_login_replays` | [Store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityLoginReplayStore.cs) inserts the original session/key/fingerprint in the owning identity transaction. [Migration 017](../../db/migrations/017_identity_login_replays.sql) supplies database `created_at`. | API UPDATE/DELETE denied; Worker expiry-only cleanup, at most 100 per invocation. Worker cannot read session/fingerprint payload. |
| `identity_registration_replays` | [Store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRegistrationReplayStore.cs) inserts the original verification-source/token pointer with database `created_at`; [migration 018](../../db/migrations/018_identity_registration_replays.sql) enforces source/token shape and subject affinity. | Same restricted expiry cleanup; token/key/fingerprint payload is not Worker-readable. |
| `identity_recovery_request_replays` | [Store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRecoveryRequestReplayStore.cs) inserts the original operation/token-source receipt; [migration 019](../../db/migrations/019_identity_recovery_request_replays.sql) supplies database `created_at`. | Subject/key/operation identity; expiry-only Worker cleanup with no token/fingerprint disclosure or UPDATE authority. |
| `identity_token_consumption_replays` | [Store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityTokenConsumptionReplayStore.cs) inserts original token identity and `consumed_at`; [migration 020](../../db/migrations/020_identity_token_consumption_replays.sql) separately supplies receipt `created_at` and checks consumption does not follow receipt creation. | Expiry cleanup is separate from consumption; neither timestamp changes on replay. API UPDATE/DELETE denied, Worker payload access denied. |
| `identity_revocation_replays` | [Store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityRevocationReplayStore.cs) inserts a completed session/operation receipt with database `created_at`. | [Migration 015](../../db/migrations/015_identity_revocation_replays.sql) permits the API to remove only its expired subject receipt before a new insertion, and Worker bounded global expiry cleanup. A reinserted expired key creates a new receipt; a live receipt is not updated or deleted. |
| `invitation_creation_replays` | [Invitation store](../../src/StrataAI.Infrastructure/Onboarding/PostgresInvitationStore.cs) inserts a token-free invitation pointer in the authorized Organization transaction; [migration 022](../../db/migrations/022_invitation_creation_replays.sql) supplies database `created_at`. | Expired keys stay reserved. Replay joins the current invitation, whose lifecycle clock remains independently mutable; it does not update the receipt or issue another invitation/token. |
| `organization_metadata_replays` | [Store](../../src/StrataAI.Infrastructure/Organizations/OrganizationMetadataReplayStores.cs) inserts the original fingerprint/result acknowledgment in the owning transaction; [migration 082](../../db/migrations/082_organization_metadata_replays.sql) supplies database `created_at`. | Expired keys stay reserved; runtime UPDATE/DELETE denied. Privileged expiry fixtures change receipt clocks deliberately and are not runtime mutation paths. |
| `organization_departure_replays` | [Store](../../src/StrataAI.Infrastructure/Organizations/OrganizationDepartureReplayStores.cs) inserts the original departure acknowledgment; [migration 083](../../db/migrations/083_organization_departure_replays.sql) supplies database `created_at`. | Expired keys stay reserved; no runtime payload UPDATE, DELETE or Worker table access. |
| `organization_removal_replays` | [Store](../../src/StrataAI.Infrastructure/Organizations/OrganizationRemovalReplayStores.cs) inserts the original fingerprint acknowledgment; [migration 084](../../db/migrations/084_organization_removal_replays.sql) supplies database `created_at`. | Same reserved-key and runtime write boundary; membership state is a separate mutable record. |
| `organization_creation_replays` | [Store](../../src/StrataAI.Infrastructure/Organizations/OrganizationCreationReplayStores.cs) inserts the original Organization result in the owning transaction; [migration 086](../../db/migrations/086_organization_creation_replays.sql) supplies database `created_at`. | Expired keys stay reserved. Replay does not edit the stored result or create a second Organization. |
| `organization_deletion_replays` | [Store](../../src/StrataAI.Infrastructure/Organizations/OrganizationDeletionReplayStores.cs) inserts the original acceptance fingerprint; [migration 087](../../db/migrations/087_organization_deletion_replays.sql) supplies database `created_at`. | Expired keys stay reserved; asynchronous deletion request/progress/steps have separate lifecycle rules and are not exempted by this receipt classification. |
| `board_filter_interaction_replays` | The current [migration-085 capability](../../db/migrations/085_interaction_actor_lock_order.sql) inserts one original source-event pointer and fingerprint, with its own database receipt time and exact 24-hour expiry. | [Migration 077](../../db/migrations/077_board_filter_interaction_replays.sql) refuses every UPDATE. The capability removes at most 100 expired receipts for the admitted actor; it retains source events and live receipts, enforces 1,000-row capacity, and rechecks current Board admission on replay. |
| `navigation_interaction_replays` | The same current capability inserts a receipt with its own database time, distinct from original navigation-event time. | [Migration 080](../../db/migrations/080_navigation_interaction_replays.sql) refuses every UPDATE. Current actor/target admission, original source identity, expiry, bounded actor cleanup and 1,000-row capacity remain enforced; API/Worker have no direct table-write authority. |

Read-only installed-catalog verification against the isolated schema-127
persistence database confirms forced RLS on all 13 tables and no UPDATE column
privilege for either runtime role. Direct DELETE is absent except Worker
cleanup on the five identity tables and API expiry replacement on revocation
receipts. Both interaction receipts have enabled UPDATE-refusal triggers.
The probe reads only catalog metadata in a read-only transaction, not receipt
rows, and changes no account, source, grants or schema.

The complete original runtime-role gate already passed in the fresh
schema-127 invocation recorded above. Its identity sections retain their
original expired/live receipt fixtures, payload-disclosure refusals, missing
cleanup-scope checks, rollback and bounded 100/100/50/0 cleanup assertions.
The original search/navigation SQL gate execution is recorded in the earlier
history classification. This reconciliation does not claim a new execution
of every Organization or identity HTTP/native retry contract; the unfiltered
persistence executable remains live and its complete result is still needed.

The classification total becomes **32/51**: 16 unconditional immutable
history/ownership candidates, two runtime-immutable mention revision tables,
13 runtime-immutable receipt payloads with the retention rules above, and one
mutable derived-clock notification table. The remaining **19** are
`attachment_preview_sweeps`, `attachment_previews`, `attachment_scan_sweeps`,
`identity_events`, `invitation_mail_intents`,
`invitation_recipient_authority_revisions`, `invitation_recipient_proofs`,
`mass_mention_reservations`, `mention_handle_reservations`,
`organization_deletion_requests`, `organization_deletion_steps`,
`organization_invitation_acceptances`, `organization_invitation_creations`,
`organization_invitation_revocations`, `organization_membership_activations`,
`organization_membership_removals`, `schema_migrations`,
`work_command_replays` and `work_events`. This is a writer/retention audit,
not an acceptance percentage or waiver of mutable-record clock provenance.
PRD-01 remains open at **34% estimated work remaining** (planning estimate).

### Organization transition proofs, deletion receipts and migration ledger

Eight further candidates have complete source writer/retention classifications.
Their immutable payload or privileged ledger semantics do not exempt their
owning mutable entities, jobs or progress records from audit-clock coverage.

| Candidate | Writer and original time | Mutation/retention boundary |
| --- | --- | --- |
| `organization_deletion_requests` | [Publisher](../../src/StrataAI.Infrastructure/Organizations/PostgresOrganizationDeletionJobPublisher.cs) inserts the accepted owner/request/version/correlation identity in the owning Organization transaction; [migration 088](../../db/migrations/088_organization_deletion_progress.sql) records database `created_at`. | Enabled BEFORE INSERT/UPDATE/DELETE guard validates insertion against actual DELETING parent and active owner, and unconditionally rejects UPDATE/DELETE. Replay verifies original identity and progress/job presence without editing the request. |
| `organization_deletion_steps` | [Migration 092](../../db/migrations/092_organization_deletion_pages.sql) inserts each applied bounded page's original checkpoint/candidate/next-step receipt with finite `completed_at`, using the same effect time as the progress transition. | Every UPDATE/DELETE is refused. Replay checks the original request and returns the retained step; it does not restamp it. The separate mutable progress record has its own creation/update clocks from migration 124. |
| `organization_membership_activations` | [Migration 097](../../db/migrations/097_organization_member_addition_events.sql) captures only an actual ACTIVE insertion or transition, with original membership version and `updated_at` as `activated_at`. | Runtime has no direct INSERT/UPDATE/DELETE access. No unconditional history guard; parent FK has ON DELETE CASCADE. Retained metadata-source FKs restrict deleting a published proof. No legacy activation events are inferred. |
| `organization_membership_removals` | [Migration 098](../../db/migrations/098_organization_member_removal_events.sql) captures actual ACTIVE→REMOVED with increasing version, previous role and admitted `updated_at` as `removed_at`. | Same private trigger insertion and parent-cascade/published-source FK boundary. Membership edits are not edits to retained removal proofs. |
| `organization_invitation_creations` | [Migration 099](../../db/migrations/099_organization_member_invitation_events.sql) captures a new internal Organization invitation's actual creation/version/time. | Private trigger insertion only; no runtime direct writes. Parent cascade is permitted subject to published-source FK restriction; old invitations receive no invented creation proofs. |
| `organization_invitation_revocations` | [Migration 100](../../db/migrations/100_organization_invitation_revocation_events.sql) captures actual unaccepted internal invitation revocation with increasing version, retaining both `revoked_at` and source `updated_at`. | No runtime direct writes or unconditional administrative UPDATE/DELETE guard. Capture does not edit earlier creation proofs or invent legacy revocation attribution. |
| `organization_invitation_acceptances` | [Migration 101](../../db/migrations/101_organization_invitation_acceptance_events.sql) captures actual unrevoked internal invitation acceptance with increasing version, exact original issuer/email/role affinity, admitted actor and source times. | Same private writer and parent-cascade/published-source FK boundary. Acceptance proof payload does not become a mutable invitation lifecycle record. |
| `schema_migrations` | [Foundation migration](../../db/migrations/001_foundation.sql) records each committed migration's `version` and `applied_at`; ordered migrations insert their ledger entries within the schema transaction. | Neither runtime role has table INSERT/UPDATE/DELETE authority. This is privileged schema deployment history, outside tenant entity routing, with no unconditional administrative mutation guard. Disposable readiness/refusal fixtures deliberately remove/restore ledger entries. |

Read-only installed-catalog verification on the fresh schema-127 security
companion database confirms forced RLS on the seven Organization tables,
enabled UPDATE/DELETE guards on both deletion receipt tables, and no runtime
UPDATE/DELETE privileges on all eight. Only the API can directly insert the
deletion request; transition proofs and steps use private owning capabilities.
Neither runtime can write the migration ledger. Source inspection includes
current producer replacements and administrative refusal/cleanup fixtures,
rather than treating a table name or timestamp alias as an invariant.

The complete unfiltered persistence executable has now finished successfully;
its default-path scope and the subsequent failed populated-database companion
attempt are recorded separately in [source verification](source-test-results.md).
All four original companion security/Reminder gates subsequently pass on their
required fresh database, without changed assertions. This does not imply
execution of special mode-only persistence branches or current immutable-image
release acceptance.

The classification total is **40/51**: 18 unconditional immutable
history/ownership/deletion-receipt candidates, two runtime-immutable mention
revision tables, 13 receipt payloads with explicit retention rules, five
private transition-proof tables with parent-cascade boundaries, one privileged
migration ledger and one mutable derived-clock notification table. **11**
remain: the two sweeps, preview manifests, identity events, invitation mail
intents, recipient authority revisions/proofs, mass-mention and handle
reservations, Work command replays and Work events.

`work_command_replays` is explicitly mutable. The
[unit of work](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkManagementUnitOfWork.cs)
inserts a pending claim and then UPDATEs `result_json` on success in the same
owning transaction; [migration 010](../../db/migrations/010_work_command_replays.sql)
stores receipt `created_at` but no completion/update timestamp. Failed commands
roll back their claims, and duplicate commands return the retained result after
current admission without an UPDATE. Atomic commit does not make the payload
UPDATE disappear or establish the historical completion time. This remains a
clock/provenance gap; neither expiry nor a guessed entity time is promoted to
an authoritative legacy completion clock. PRD-01 stays open at **34% estimated
work remaining** (planning estimate).

### Final candidate writer classification and remaining clock repairs

The remaining eleven candidates have now been traced through their current
runtime producers, migration replacements and refusal/cleanup fixtures. Five
have immutable runtime payloads; their retention boundaries remain explicit.

| Candidate | Actual writer and time | Retention/verification boundary |
| --- | --- | --- |
| `attachment_previews` | The private [manifest capability](../../db/migrations/045_attachment_preview_intents.sql) inserts the original declared encoding, source revision and creation time under the owning preview job/lease. Its guard refuses changed fields but allows exact no-op UPDATE. | Runtime direct writes are denied. There is no unconditional DELETE guard; published [publication receipts](../../db/migrations/046_attachment_preview_publication.sql) FK-protect their owning manifests. An unpublished manifest has a different privileged cleanup boundary. The completed default persistence path includes original manifest/preview contracts; external encoding/provider acceptance is separate. |
| `identity_events` | [Identity store](../../src/StrataAI.Infrastructure/Identity/PostgresIdentityStore.cs) inserts each original subject/sequence/type/version/correlation fact in its owning command; [migration 012](../../db/migrations/012_identity_events.sql) supplies database creation time. | API SELECT/INSERT only; Worker cannot read these global payloads. The [migration-109 history trigger](../../db/migrations/109_invitation_issuer_account_authority.sql) guards only USER_DEACTIVATED, not every event type. General administrative UPDATE/DELETE refusal is not claimed. Identity stream counters remain independently mutable and already have their own clocks. |
| `invitation_recipient_proofs` | [Migration 102](../../db/migrations/102_invitation_recipient_events.sql) captures actual admitted creation/acceptance/revocation with the source invitation's exact version, routing identity and `updated_at` as proof `created_at`. | The current [migration-103 guard](../../db/migrations/103_invitation_recipient_unpublished_cleanup.sql) refuses UPDATE and direct DELETE, but permits nested parent-cascade deletion only before any corresponding journal publication. Published facts remain retained; this is not unconditional deletion refusal. Earlier complete recipient SQL verification is recorded above. |
| `mention_handle_reservations` | [Migration 056](../../db/migrations/056_mention_handles.sql) reserves the original owner/time at registry seeding, account insertion or an admitted handle revision. Former aliases remain reserved; changing a current handle does not update old reservation payloads. | Changed-field UPDATE is refused and exact no-ops are allowed. No runtime table writes or payload reads; private trigger writers insert only. User FK permits privileged parent cascade and there is no unconditional DELETE guard. Current handle entities have separate creation/update/version clocks. |
| `mass_mention_reservations` | [Quota store](../../src/StrataAI.Infrastructure/WorkManagement/CardMassMentionQuota.cs) inserts the exact original mention-event/Card/actor/source-time identity. [Migration 061](../../db/migrations/061_mass_mention_quota.sql) serializes at the Board, validates the source, and supplies database `reserved_at`. | Every UPDATE is refused; API SELECT/INSERT only and Worker access denied. No unconditional DELETE guard. The original persistence quota fixture explicitly disables its UPDATE guard to age disposable reservations, restores it and cleans up; that is not runtime clock mutation. |

The other six candidates are **mutable** and remain clock/provenance repairs,
even though their writer classification is complete. Each needs a migration,
current writer/read contract, upgrade/refusal evidence and broader regression
verification before the timestamp requirement can be satisfied.

| Candidate | Current mutable writer | Unresolved clock/provenance requirement |
| --- | --- | --- |
| `attachment_preview_sweeps` | Current [migration-051 backfill capability](../../db/migrations/051_attachment_lifecycle.sql) inserts one tenant checkpoint, seeks bounded candidate pages, and updates or resets `(cursor_created_at,cursor_id)` under its tenant/checkpoint locks. | No checkpoint creation/update time is retained. Candidate attachment creation time is a seek key, not checkpoint creation or the last reset time; null reset cannot reconstruct legacy history. |
| `attachment_scan_sweeps` | Current [migration-052 recovery capability](../../db/migrations/052_attachment_lifecycle_scan.sql) inserts and advances/resets the tenant checkpoint while fencing recovered job/source effects. | Job creation time is a seek key, not checkpoint audit time. Existing checkpoint creation/last-reset provenance is absent; original budgets, tenant locks and recovery fences must remain. |
| `invitation_mail_intents` | [Mail publisher](../../src/StrataAI.Infrastructure/Onboarding/PostgresInvitationMailPublisher.cs) inserts a pending intent; [finish capability](../../db/migrations/023_invitation_mail_intents.sql) records terminal state/receipt/error, database `finished_at` and increasing version after original job/lease admission. | Pending creation and terminal finish times exist, but no general update clock or guard proves that those cover every historically admitted payload edit. Original privileged scope fixtures alter pending target fields without recording a new clock. A `COALESCE(finished_at,created_at)` alias alone is not whole historical mutation proof. |
| `invitation_recipient_authority_revisions` | Current [migration-109 publisher](../../db/migrations/109_invitation_issuer_account_authority.sql) increments once for each admitted tenant/source/email effect, with a separate global issuer-event/email deduplication step for User effects. | Neither first counter publication nor subsequent counter effects have recorded publication times/sequences. Source time is not later delivery time; globally deduplicated User effects cannot be counted as every tenant effect. Legacy creation/last increment clocks cannot be invented from a source maximum. |
| `work_command_replays` | [Work unit of work](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkManagementUnitOfWork.cs) inserts a claim and UPDATEs successful `result_json` in the owning transaction; refused commands roll back and exact replay does not UPDATE. | Receipt creation exists; completion/update time is absent. Atomic commit is not a historical completion timestamp, and receipt expiry is not the update clock. Existing results must not be rewritten or treated as pending to manufacture provenance. |
| `work_events` | [Event store](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkEventStore.cs) inserts immutable payload/source time; [delivery store](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkEventDeliveryStore.cs) UPDATEs null readiness under the actual live job/worker/lease fence, verifies again and commits. | [Migration 062](../../db/migrations/062_activity_attribution.sql) protects payload fields but excludes readiness. Original privileged source fixtures can clear readiness after publication; `COALESCE(ready_at,created_at)` would lose that last mutation/history. Source insertion and delivery state need distinct truthful clocks without weakening final lease fencing. |

The **source writer/retention audit covers all 51 candidates**. That conclusion
is deliberately separate from timestamp implementation: six mutable
candidate-table repairs remain unresolved, and their legacy provenance and
full acceptance verification are still required. Other mutable entities,
navigation, capacity, accessibility, all special-mode persistence invocations
and current immutable-image release evidence remain within PRD-01's original
scope. No clock is backfilled from migration time, a lease expiry or an
unrelated source timestamp; no acceptance criterion or scope is changed.
The full 32-case Board browser invocation remains active. PRD-01 remains open
at **34% estimated work remaining** (planning estimate).
