# Current Organization surface admission (ARCH-02-AC-003)

GET `/organizations/{id}/surface-access?surface=INTERNAL|PORTAL` is an
authenticated, current admission read. Success returns only `organizationId`
and the requested `surface`; it contains no Organization name, member profile,
role or invitation material. Missing/inactive Organization or absent requested
grant returns the existing non-disclosing `organization_not_found` problem.
An invalid surface returns `invalid_access_surface` (400). Missing/revoked
session remains 401; storage failure remains the existing safe 503 problem.

Internal admission requires active internal membership. Portal admission
requires an active row in the separate `portal_access` store. Neither grant
implies the other, including for internal Organization owners. This read creates
no membership, invitation, audit event, durable job or transferable capability.
Every protected API continues to authorize its own operation independently.
Public Board viewing is a separate Board admission decision.

The existing Organization unit of work admits an active parent and current
account/session in the tenant command transaction. Internal membership is read
after its existing lock. Portal rows are read with a scoped `FOR SHARE` lock,
then the actor/session is checked again before returning. Forced RLS and the
restricted runtime database role continue to apply. No schema, provider,
framework or deployable process was added.

The API-host regression covers internal-only, Portal-only, unrelated scope,
independent dual grants, removal of internal membership without removing Portal
access, inactive Organization, invalid input, minimal response fields and logout.
The solution builds with zero warnings/errors; Windows Application Control
prevents local test execution, so Linux execution remains required.

The endpoint is the server contract for upcoming shell admission. The current
InternalAppShell and PortalShell are not yet wired to it. Client scope changes,
stale reads, public Board viewing and exact-image Portal-only deep-link tests
remain required before ARCH-02 closure.
