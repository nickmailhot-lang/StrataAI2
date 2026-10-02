# Work command authorization and lifecycle scopes

Work Management commands now authorize both keyed and unkeyed intent inside the
owning command transaction. PostgreSQL acquires locks in this order:

1. Active organization row (`FOR SHARE`).
2. Actor's organization membership, when present (`FOR SHARE`).
3. Board row, when the command targets an existing board (`FOR UPDATE`).
4. Actor's board membership, when present (`FOR SHARE`).

The application then re-reads permissions and lifecycle under READ COMMITTED.
These locks survive until the root command commits or rolls back. SHARE prevents
non-key status and role updates as well as removal. The board gate precedes child
and event-stream locks; default rank allocation reuses the same transaction.
Existing board commands serialize within that board, while different boards can
proceed independently. New board creation holds the organization/membership scope.
Cross-board movement will need a deterministic multi-board locking contract.

A revocation or archive that already owns its relevant row is observed after the
waiting command acquires the lock. Failed authorization/lifecycle returns the
existing masked not-found code and commits no mutation, audit, event, job, or retry
result. Conversely, once a command holds its authorization scope, a concurrent
revocation waits for that command's commit. An acknowledged revocation therefore
cannot be followed by a newly accepted write using the old membership state.

Archived organizations expose read-only board permissions. Deleting organizations
deny board snapshots and synchronization, including previously public boards.
Authenticated snapshots acquire a dedicated Board read scope: the Organization
SHARE lock accepts ACTIVE or ARCHIVED, followed by the same membership and Board
locks as command admission. Fresh view authorization and final session verification
remain mandatory. Write admission continues to require ACTIVE. This restores the
archived-Organization snapshot contract after member previews introduced locked
snapshot reads. Exact-image run 37052462546 exposed the regression at the existing
read-only assertion in `test-work-command-scopes.sh`; the expanded fixture also
checks an ordinary member's frozen permissions and observed read lock waits during
Board membership removal and Organization deletion. The repair's runtime evidence
must pass CI before related acceptance criteria can be claimed complete.
An archived source list blocks card edits, moves, archive and restoration. A
deleted source list blocks all card lifecycle commands. List lifecycle changes
require an active board. Permanent card deletion requires administrator permission
and archived card state; an administrator can delete an already archived card in
an archived list while its board is active. Retry authorization also requires the
current administrator permission for deletion.

The API host tests cover fresh/keyed/replayed intent during organization deletion,
anonymous public-board denial, child edits/moves/restoration under archived parents,
and member versus administrator card deletion. Exact-image PostgreSQL CI holds
real row locks, observes live lock waits, commits archive/revocation changes, and
compares protected child state plus audit/event/job/replay counts. A held audit
insert verifies the opposite ordering: a membership suspension cannot commit
while an already authorized write retains its scope.

Demo commands share a process semaphore and re-read current authorization. Demo
does not claim PostgreSQL isolation or durability across organization-service
writes; the real database fixtures establish the production locking guarantees.

Still required before related ticket closure: explicit destructive confirmation
and impact flows, complete archived-item interfaces, cross-board movement,
transactional boundaries for organization/onboarding and other modules, and the
remaining lifecycle, audit/retention, access-control and performance criteria.
