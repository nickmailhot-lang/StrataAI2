# Board member directory authority

The existing `GET /boards/{boardId}/members` now executes within the owning Board
command transaction. Its routing lookup is only a hint: current Organization and
actor membership, Board and Board-member locks precede administration admission.
The current account/session is verified before the read and again before returning
member rows. Reads deliberately use no command retry key, including when a caller
supplies one, and create no replay receipt, audit, event or job.

Host coverage extends Board administrator continuity: an explicit Board Admin
can read the directory, loses directory access after demotion to Member, and an
Organization Owner retains recovery administration even without an explicit
Board membership. Required exact-image PostgreSQL coverage now waits on the real
Board lock, commits a deleted lifecycle, and requires safe directory denial with
unchanged invitation/audit/event/stream/job/replay state. Local warnings-as-errors
build and shell syntax checks pass; host and PostgreSQL execution remain pending
Linux CI because local test executables are blocked by Windows Application Control.

Member-management UI, bounded member directory pagination and complete PRD-05
acceptance evidence remain outstanding. This change does not close the ticket or
claim unbounded member reads satisfy large-directory requirements.
