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

The MUI Archived Lists page is linked from active Board administration and supports
previous/next pages, empty/loading/error states and SignalR/polling revalidation.
A restore dialog reviews the name and card count, explains preserved position/card
states and checks fresh data before sending the original version with a new key.
A changed review requires cancellation and fresh review. Lost or malformed
acknowledgments retain the original key/version even when the List has disappeared
from the canonical archive; only that same restore can be retried. Denied authority
clears the review, and scoped reads/writes are bounded and abort on route changes.
Focus returns to the refresh control after the dialog exits and current reads settle.

Fifteen component checks cover scoped restore acknowledgment, loss/retry after
canonical removal, mismatched acknowledgments, stale consent, malformed pages,
denied persistence, cancellation, read-page recovery and the request deadline.
An unresolved dialog can explicitly recheck current authority after a failed
canonical read without discarding or changing its original restore intent.
Desktop/phone release-browser cases deliberately lose a committed restore response,
retry it unchanged and verify two-client recovery, unchanged cards/neighbor/rank,
preserved archived-card exclusion, exact version increments, focus and reload.
These browser cases require actual CI execution; collection alone proves no runtime
behavior.

The active Board now offers an administration-only archive dialog with explicit
List selection and reversible-impact review. Changed canonical versions require
another explicit review; other unresolved List operations block that choice.
Its owner lives outside the canvas, so an unresolved original key/version remains
recoverable after a committed archive removes its column. While unresolved, other
Board writes are blocked but its own retry and fresh-read control remain available.
Thirteen focused component regressions cover selection, exact acknowledgments,
column disappearance, changed consent, permission denial and the request deadline.
The same two release-browser cases now archive through the real UI, lose/recover
that response and then restore through the archive page. Archived-card browsing
and permanent-delete confirmation/contained-card impact still require work before
PRD-07/18 can close.
