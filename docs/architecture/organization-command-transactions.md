# Organization command transactions

Organization creation, metadata changes, member removal, departure and deletion requests use one Application unit-of-work boundary. In PostgreSQL, store operations borrow its tenant session; their local commit calls cannot commit the owning transaction. The final success commits both the mutation and its audit. Failure or an audit exception rolls back both. Database failures return a masked `organization_storage_unavailable` response with HTTP 503.

For existing organizations, the boundary locks the active organization row for update before reading permissions or changing membership. It then locks existing actor/target membership rows in user-ID order. Authorization runs after those waits. Concurrent departures serialize at the organization gate, so the second command counts the first command's committed membership change and preserves the last active owner. These parent-before-membership locks follow the Work command lock order and exclude Work writes while deletion or organization membership commands commit.

The required exact-image PostgreSQL fixture rejects each mutation when audit insertion is denied and compares organization/membership state, audit count and the creator's organization count. It also observes a real runtime database lock wait before revoking an actor, and observes two concurrent departures waiting at the parent gate before checking one success, one sole-owner rejection, one remaining owner and one audit insertion. API host tests cover the owner floor and rejecting changes/departure after deletion begins.

Demo Organization commands acquire the process-local account/Organization gate followed by the shared Work command gate. The demo audit store remains a no-op; this is not durable transaction or cross-process evidence. Direct store writes, invitations, future role/ownership-transfer commands and complete organization deletion processing require their own explicit command policy. Durable retry keys, organization archive/restore, audit browsing, administration UI and full acceptance criteria remain separate unfinished work. This increment does not declare PRD-03, PRD-18 or the security architecture complete.

## Demo rollback and final actor admission

Demo Organization commands now acquire the account/Organization gate before the
Work gate. Work transactions use that same Work gate, while navigation continues
to acquire it from an already owning identity transaction. The two gates remain
separate: reacquiring the account gate inside navigation would deadlock. Both
remain process-local and provide no cross-host or durable database evidence.

Within those gates, the Organization command enters the owning Work scope and
captures Organization records/memberships, invitation rows, creation retry
receipts and Portal grants plus registered Work rollback participants. These include Work records, assignment state, events, notifications,
mention quotas, reminder rows and Demo reminder jobs. Failure results, exceptions,
cancellation and a refused final actor check restore the snapshots in reverse
order before releasing the gates. Successful departures intentionally retire the
old membership; the final admission checks the actor/session rather than requiring
that departed grant to remain active. A successful command commits only after
that check and an explicit cancellation check.

The API-host regression contains successful commit, failure result, post-mutation
actor refusal, exception and cancellation cases. It checks Organization,
membership and Board state and verifies that a fresh command can acquire the
gates afterward. A coordinated concurrency case queues a Work operation while an
Organization mutation is uncommitted, requires it to stay outside the operation,
then verifies that Organization rollback restores its name and preserves the
subsequent Work commit. Runtime execution of these new cases is required in CI;
compilation alone does not establish their acceptance.

A further member-removal/departure regression decorates the real Demo event store and
withdraws actor admission only after its removal event has been appended. It
requires the final refusal to restore membership, assignment, Card revision and
event stream state, then verifies that a fresh authorized removal or departure commits. This
case also awaits native CI execution. A deletion regression similarly withdraws
admission after the real reminder cancellation event; it requires Organization,
reminder generation/version/trigger and event state to be restored, then checks
that an authorized retry suspends the reminder without changing the Card.

A navigation contention case queues the owning identity operation behind an
uncommitted Organization command. After rollback it requires the canonical
Board observation and immutable replay to complete through the Work gate,
without changing Organization or Board state. Ten-second observation bounds
make a deadlock a test failure rather than an unbounded wait. Native execution
remains required for this case as well.

Invitation stores are Organization-only participants: they are captured while
both gates are held, rather than by Work-only commands that do not exclude
identity operations. Nine new invitation cases require creation/receipt rollback
on final actor refusal, exceptions and cancellation, same-key retry/replay, and
Internal/Portal/Board token-and-grant restoration after post-consumption refusal.
They compile but await native runtime execution.

Additional native coverage is still needed for reminder job rollback. Specialized
account lifecycle cleanup, identity-owned registration with invitations and
trusted direct store mutations retain their own boundaries;
this change does not establish universal Demo transaction integrity. Production
PostgreSQL retains its owning transaction, final actor check and rollback behavior.
Full PRD-03 command integrity and acceptance remain incomplete.
