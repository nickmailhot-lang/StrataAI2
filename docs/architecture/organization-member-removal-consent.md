# Membership removal consent

`DELETE /organizations/{organizationId}/members/{userId}?expectedVersion={version}` accepts the membership version returned by the administrator directory. Existing callers that omit the query parameter retain their previous behavior. The member administration screen must supply the observed version.

The Organization command transaction locks the parent and actor/target memberships and verifies current authorization before comparing versions. An unauthorized actor receives the existing scope/permission denial; a stale authorized request receives `409 member_version_conflict`. Nonpositive versions receive `400 invalid_member_version`. Neither rejection changes membership or appends a removal audit event. Current versions still obey the usable-owner floor and Admin restrictions on removing Owners.

A conflict requires a fresh membership review and renewed confirmation. This precondition does not provide a durable command receipt or authorize automatic retries after a missing acknowledgment. Pagination alone cannot prove a target was removed.

Validation includes an API-host role-change case, a browser-context stale/current version scenario, and a mandatory exact-image PostgreSQL fixture that commits a role/version change while removal waits on the Organization parent lock, then checks the conflict, unchanged active membership, and unchanged audit count. The local solution build and browser scenario pass; API-host execution and the database fixture require Linux CI. The full member administration UI remains outstanding.
