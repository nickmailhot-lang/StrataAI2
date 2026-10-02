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

InternalAppShell and PortalShell now withhold protected navigation/content until
the requested current surface read succeeds with matching Organization/surface.
Organization changes immediately retire previous admission; aborted or late old
responses cannot admit the new scope. Reads have a five-second deadline and an
explicit fresh-check action. Focus and ten-second polling recheck current grants.
Background success retains the mounted child to preserve Board/detail context;
denial or error removes protected shell content. These checks supplement server
authorization and do not promise instant detection between checks.

Only exact Board/Card viewing paths can fall back to an independent Board screen
after denied internal admission. That screen obtains its own authorized snapshot
without Council navigation; administration/invitation/settings paths cannot use
the fallback. Storage/network/malformed-admission errors do not enable fallback.

Unit coverage includes pending/denied content, wrong-surface responses, late
old-scope replies, current revocation on focus, independent Board fallback and
bounded timeout/recheck. The exact-image browser case covers Portal-only deep
links, private denial, public read-only viewing, separate internal grant/removal,
retained Portal grant and logout at desktop/phone widths. Local unit tests,
typecheck/lint and collection pass; exact-image execution remains required before
ARCH-02 closure.
