# Archived List discovery (PRD-07 / PRD-18)

`GET /boards/{id}/archived-lists?after={uuid}` is a separate authenticated archive
read. The active canvas stays unchanged and excludes archived Lists and their
contained cards. Archive discovery requires current Board administration rights
and an active Organization/Board, matching the existing List restore policy.
Portal access and public Board visibility do not grant archive administration.
Denied callers receive the usual minimal Board-not-found response. Invalid
cursors are validated only after authorization.

The response contains Organization/Board IDs, up to fifty archived List records,
their contained-card counts and a nullable UUID continuation cursor. Counts include
active and archived cards associated with that List, exclude product-deleted
cards and disclose no card titles or bodies. Active/deleted Lists are excluded.
UUID ordering and strict greater-than continuation prevent duplicate entries
while the collection is unchanged. This is live seek paging, not a frozen archive
snapshot: concurrent archive/restore can change later pages. Restart discovery to
see newly archived entries preceding an existing cursor.

Production reads use the existing forced tenant RLS and explicit tenant/Board
predicates. Each request holds Organization, actor membership and Board authority
locks in the existing command order, then rechecks current actor/session before
returning. Later pages require fresh authority too. No mutation key is consumed;
archive discovery writes no entities, audit entries, events, jobs or receipts.

Host checks exercise fifty-plus paging, complete IDs, nonempty card counts,
invalid cursors, outsider/anonymous denial and removal after restore. A required
exact-image PostgreSQL fixture adds 52 archived Lists with active/archived/deleted
cards, exclusion and state checks, Portal/public separation and observed Board
lock waits followed by Board administrator removal, session revocation and
parent archive. The build and shell syntax checks pass locally; execution of
the host and real PostgreSQL cases is required in Linux CI.

The Archived Items interface, archived-card discovery and restore/delete controls
are still required. Permanent deletion must also add explicit confirmation and
contained-card impact disclosure before PRD-07/18 can close. This read establishes
the authorized, bounded List collection those controls need.
