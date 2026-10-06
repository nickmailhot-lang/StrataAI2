# Organization command transactions

Organization creation, metadata changes, member removal, departure and deletion requests use one Application unit-of-work boundary. In PostgreSQL, store operations borrow its tenant session; their local commit calls cannot commit the owning transaction. The final success commits both the mutation and its audit. Failure or an audit exception rolls back both. Database failures return a masked `organization_storage_unavailable` response with HTTP 503.

For existing organizations, the boundary locks the active organization row for update before reading permissions or changing membership. It then locks existing actor/target membership rows in user-ID order. Authorization runs after those waits. Concurrent departures serialize at the organization gate, so the second command counts the first command's committed membership change and preserves the last active owner. These parent-before-membership locks follow the Work command lock order and exclude Work writes while deletion or organization membership commands commit.

The required exact-image PostgreSQL fixture rejects each mutation when audit insertion is denied and compares organization/membership state, audit count and the creator's organization count. It also observes a real runtime database lock wait before revoking an actor, and observes two concurrent departures waiting at the parent gate before checking one success, one sole-owner rejection, one remaining owner and one audit insertion. API host tests cover the owner floor and rejecting changes/departure after deletion begins.

Demo commands serialize through a process-local semaphore. The demo audit store remains a no-op; this is not durable transaction or cross-process evidence. Direct store writes, invitations, future role/ownership-transfer commands and complete organization deletion processing require their own explicit command policy. Durable retry keys, organization archive/restore, audit browsing, administration UI and full acceptance criteria remain separate unfinished work. This increment does not declare PRD-03, PRD-18 or the security architecture complete.

## Remaining Demo transaction integrity

The current Demo Organization unit verifies the actor before invoking its
operation but does not repeat PostgreSQL's final actor verification or capture
rollback participants. This leaves an acceptance gap for expiry, cancellation
or an exception after mutation. The affected scope includes Organization records
and memberships, Card assignment removal/events on member departure/removal,
and reminder rescheduling after deletion admission. Serialization alone does
not prove rollback or a final current-session fence.

A repair must coordinate the account/Organization gate with the existing Demo
Work transaction participants and their owning Work scope. Work transactions
currently use a separate semaphore; restoring a broad snapshot without shared
transaction coordination could overwrite concurrent Work changes. Adding only
late actor refusal could also return an error after retained partial mutations.
Verification must cover success, post-operation actor refusal, exceptions,
cancellation and concurrent Work/Organization changes before this gap is closed.
Production PostgreSQL retains its existing owning transaction, final actor check
and rollback behavior. Full PRD-03 command integrity remains incomplete.
