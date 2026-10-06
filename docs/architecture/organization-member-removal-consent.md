# Membership removal consent

`DELETE /organizations/{organizationId}/members/{userId}?expectedVersion={version}` accepts the membership version returned by the administrator directory. Existing callers that omit the query parameter retain their previous behavior. The member administration screen must supply the observed version.

The Organization command transaction locks the parent and actor/target memberships and verifies current authorization before comparing versions. An unauthorized actor receives the existing scope/permission denial; a stale authorized request receives `409 member_version_conflict`. Nonpositive versions receive `400 invalid_member_version`. Neither rejection changes membership or appends a removal audit event. Current versions still obey the usable-owner floor and Admin restrictions on removing Owners.

A conflict requires a fresh membership review and renewed confirmation. This precondition binds the reviewed membership version. The optional durable retry contract below recovers an original acknowledgment after a missing response. Pagination alone cannot prove a target was removed.

Validation includes an API-host role-change case, a browser-context stale/current version scenario, and a mandatory exact-image PostgreSQL fixture that commits a role/version change while removal waits on the Organization parent lock, then checks the conflict, unchanged active membership, and unchanged audit count. The local solution build and browser scenario pass; API-host execution and the database fixture require Linux CI. The full member administration UI remains outstanding.


## Durable removal acknowledgments

The DELETE endpoint accepts an optional nonempty UUID Idempotency-Key and an
optional expectedActorId query parameter. Account mismatch returns
session_unavailable before receipt lookup or mutation. The owning Organization
transaction serializes the actor/target command and atomically commits removal,
assignment cleanup, audit and an immutable receipt. Its fingerprint binds the
Organization, target and exact reviewed version (including an omitted version).
Identical replay returns 204 without removing a later rejoined membership;
changing target/version with that key returns idempotency_conflict. After 24
hours the key returns idempotency_expired and stays reserved.

Current administration is required before another member's receipt is consulted.
Self-removal may acknowledge only the current account's original token-free
command despite its retired membership. An active Organization and current
session remain mandatory. Replay grants no access or membership state disclosure.
Fresh commands still enforce target version, Admin/Owner safeguards and the
usable-owner floor. Unkeyed callers retain their existing behavior.

Migration 084 forces tenant RLS; the API receives SELECT/INSERT only and the
Worker has no receipt access. Both hosts require its ledger entry. The Demo
store participates in the owning Organization rollback. Retention/cleanup policy
and browser same-key recovery remain outstanding; the current screen reconciles
current membership rather than using these receipts.

API-host cases cover concurrent replay, later rejoin, changed target/version,
account mismatch, demotion and self-removal/revoked-session recovery. Mandatory
native checks deny receipt INSERT and compare membership/audit/receipt state,
observe concurrent requests waiting on the parent, require one audit/receipt,
and verify rejoin preservation, conflict, expiry and restricted tenant/role access.
Compilation and script syntax are source checks; actual execution requires CI.

The Demo cross-store rollback case now also uses a keyed removal. Final actor
loss must leave no removal receipt while restoring membership, Card revision,
assignment and Work events; the same key subsequently commits after restored
admission. Native post-publication session expiry and assignment rollback
evidence remain pending, alongside browser same-key recovery.
