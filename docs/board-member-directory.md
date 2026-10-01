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

## Bounded HTTP directory

The HTTP directory returns at most 50 active members ordered by user UUID. The
response remains an array; when a 51st row exists, `X-StrataAI-Next-Cursor` contains
the last returned user UUID. Supply it as `?after={uuid}` for the next page. The
header is absent on a terminal page. Invalid or empty UUID cursors receive the
stable `invalid_board_member_cursor` error. Cursors are positions, not grants;
every page repeats current administration and account/session admission.

Both stores apply the seek and 51-row limit for the directory. Internal safeguard
reads deliberately retain their complete membership set, so paging cannot hide
an administrator from removal/demotion continuity checks. No schema change is
needed; the existing Board/user membership key supports this access pattern.

A host case uses 53 synthetic Demo members to assert bounded results, cursor
identity, terminal-header absence, complete ordering and no duplicate UUIDs. It
does not prove production FK eligibility or PostgreSQL paging execution. Local
warnings-as-errors build passes; Linux host execution and exact PostgreSQL paging
evidence remain pending. Member-management UI and complete PRD-05 acceptance
evidence remain outstanding. The ticket remains open.
