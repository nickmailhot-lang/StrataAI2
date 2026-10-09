# Board background ownership clock audit

This records one candidate in the [PRD-01 mutable-clock audit](prd-01-acceptance.md).
It does not establish complete FOUND-FR-009 coverage.

## Owning writers and state

`board_background_images` is an immutable published-preview ownership fact.
[Migration 072](../../db/migrations/072_board_background_images.sql) defines its
creation timestamp, tenant/Board/creator/publication/source composite foreign
keys and `board_background_image_owner_guard`. The guard rejects every UPDATE
and DELETE with SQLSTATE 23514, including attempted no-op updates. INSERT must
reference a publication belonging to this Board, with creation at or after
publication, or an earlier owner on another Board in the same Organization,
with the same preview and creation at or after that owner's creation.

The production store's only ownership writer is
[CreateBoardBackgroundImageAsync](../../src/StrataAI.Infrastructure/WorkManagement/BoardBackgroundImageStore.cs).
It inserts a new identity and creation fact after matching the published
preview's size, hash and dimensions inside the owning tenant command. It does
not update an existing owner. Source inspection found two application callers:

- [Image selection](../../src/StrataAI.Application/WorkManagement/BoardBackgroundImageSelectionService.cs)
  creates an owner after admission/version/public-visibility checks and preview
  revalidation. The same command updates the canonical Board with the same
  normalized timestamp, increments its version, and appends audit/event facts.
- [Board copy](../../src/StrataAI.Application/WorkManagement/BoardCopyService.cs)
  creates a new owner referencing the source ownership fact, using the newly
  created Board's creation timestamp. Initialization accepts only version 1,
  equal Board creation/update timestamps and an unselected color background.
  This initializes the new Board; it does not mutate source ownership history.

Later selection changes belong to `boards`, whose
[UpdateBoardInternalAsync](../../src/StrataAI.Infrastructure/WorkManagement/PostgresWorkManagementStore.cs)
sets `updated_at` and increments `version` under the expected-version condition.
The selection guard and composite owned-scope foreign key bind the selected
image to this Board. Replacing the selection leaves prior ownership facts
retained. This table therefore needs no independent mutable update clock; the
canonical Board still remains in the all-mutable-record audit.

## Executed metadata evidence and limits

On October 9, 2026, five read-only catalog/permission checks passed against the
fresh local schema-116 PostgreSQL fixture used by the complete Board/tablet
phase. They confirmed the enabled BEFORE INSERT/UPDATE/DELETE row trigger,
forced tenant RLS, API SELECT/INSERT with UPDATE/DELETE denied, Worker denial
of SELECT/INSERT/UPDATE/DELETE, and the schema-116 ledger entry. No fixture rows
were changed by this inspection. Private probe and output remain outside the
repository in `board-tablet-schema116-native-20261009`.

This is source classification plus installed metadata evidence, not a newly
executed ownership-tamper contract or current immutable-image CI pass. It does
not classify route projections, leases, sweep cursors or revision counters.
In particular, the recipient revision-counter historical delivery-clock gap
remains unresolved. No historical timestamp backfill is introduced here.
