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

The mandatory `test-board-admin-continuity.sh` release fixture now also creates a
separate 53-member Board using disposable users and Organization memberships that
satisfy real foreign keys. It reads 50 then three rows through the restricted API,
checks complete ordering/uniqueness and terminal cursor absence, rejects an invalid
cursor, and compares audit/event/job/replay counts before and after reads. A UUID
retry header on GET must not create a receipt. These are administrative fixtures,
not evidence of signup, verification delivery or invitation acceptance. Shell
syntax passes locally; execution remains pending exact-image CI.

## Scoped member labels

Directory rows now use a separate read model, retaining existing membership
fields and adding `displayName`, `email` and `organizationMemberActive`. Each
bounded row resolves its profile through the owning Organization's active-member
query inside the authorized Board transaction. Board administrators can identify
their Board's participants without access to an Organization-wide directory.
Removed Organization membership yields null name/email and a false marker; the
physical Board membership remains visible for cleanup without reviving access.
Passwords, global account details, proof and session data are never projected.
Membership mutation responses and persistence records remain unchanged.

Host and mandatory PostgreSQL cases assert current scoped profile labels and
null labels after Organization membership removal. Build and shell syntax checks
pass locally; execution is pending Linux CI. Profile enrichment is bounded to the
51-row seek window but currently performs per-row scoped queries. Performance
evidence and any necessary batch-query optimization remain to be completed before
the PRD performance target can be claimed.

## Membership version consent

Board role PATCH and member DELETE accept an optional `If-Match` header containing
a positive membership version, plain or quoted. Invalid headers produce
`invalid_member_version`. After current administrative admission and the Board
lock, the command compares the active target membership with that version before
changing/removing it. Stale or missing current membership rejects with
`version_conflict`; last-administrator and target eligibility safeguards remain.

Supplied versions participate in retry fingerprints. A successfully completed
keyed retry returns its previous acknowledgment after current actor admission;
it does not reapply a role change or removal to a later membership state. Calls
without a precondition preserve the existing behavior and fingerprint format.
The upcoming management UI must always send the reviewed version and obtain
fresh consent after a conflict. The optional compatibility path must not be
described as mandatory concurrency protection for every API caller.

Two host cases cover stale role/removal consent and successful keyed retry after
a later member change. Warnings-as-errors build passes. Linux host execution,
real PostgreSQL consent checks and the management UI remain pending.

The required Board continuity release fixture now exercises consent through the
restricted PostgreSQL API: stale PATCH and DELETE must return `version_conflict`,
malformed versions must return `invalid_member_version`, and member/audit/event/
job/replay state must remain unchanged. A successful guarded role change is then
superseded; replay must return the original acknowledgment without changing the
newer role. A guarded removal followed by explicit re-addition similarly replays
its 204 acknowledgment without removing the new membership. Local shell syntax
and diff checks pass; actual fixture execution remains pending exact-image CI.

## Batched current profiles

The directory now supplies at most 51 selected Board-member UUIDs to one owning
Organization profile query. Filtering occurs before the Organization directory's
51-row limit, so Board members beyond its first page retain their current names
and emails. The SQL stays in the already authorized tenant transaction with forced
RLS, active membership and an explicit tenant predicate. Former membership still
returns null profile details even if the user is active in another Organization.
No read receipt/audit/event/job is introduced. Demo storage applies the same bounded
filter but does not claim production transaction guarantees.

A host case selects Board participants beyond the first 51 Organization members
and verifies that unrelated active membership cannot supply a former profile.
The required real-PostgreSQL fixture also gives its former member a membership in
another Organization before checking null profile fields. The earlier 94dbcbb
release baseline verified paging, current/former profiles and consent; it predates
this batch query and the added cross-Organization fixture. Current warnings-as-
errors build and shell syntax checks pass; Linux execution remains pending. This
reduces profile round trips but does not establish a latency or large-data target.