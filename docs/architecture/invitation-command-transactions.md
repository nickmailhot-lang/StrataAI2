# Invitation command transactions

Create, revoke and accept invitations run in the organization unit of work. Its active organization gate precedes actor/issuer membership locks and invitation consumption. Token routing and the recipient identity read borrow the same PostgreSQL transaction; identity reads take a share lock inside that transaction. Audit insertion, membership/PortalAccess changes and token consumption commit together. An audit failure rolls everything back and returns a masked `invitation_storage_unavailable` HTTP 503.

Acceptance checks the authenticated verified recipient's current active account and normalized email again inside the transaction. It also rechecks the original issuer's active account and active administrative membership after waits. Internal OWNER grants require a current organization owner both when issued and accepted. Portal OWNER is a separate relationship type; it does not grant internal membership. An internal invitation targeting a lower role cannot replace active OWNER membership: it returns `ownership_change_requires_confirmation` without consuming the token. Ownership removal must use the explicit organization governance workflow and its continuity checks.

The required organization command fixture now also denies audit insertion during invitation creation/revocation and both Internal/Portal acceptance, compares membership/invitation/audit/PortalAccess state, retries valid tokens, rejects duplicate acceptance, and checks Portal isolation and ownership protection. Controlled locks prove that issuer revocation and recipient account deactivation committed during acceptance waits reject the command without consuming the token, changing membership version or appending an audit. Host tests cover role grants, surface separation, stale issuer permission, ownership and single-use acceptance.

This increment retains the adopted PostgreSQL/RLS architecture and adds no schema migration. Demo serialization remains process-local with no durable audit guarantee. Organization-owned invitation commands now snapshot invitation rows, creation receipts and Portal grants together with Organization/Work participants, and restore them on failure, exceptions, cancellation or final actor refusal. Durable request retry keys, invitation delivery, pagination, accessible administration screens, complete organization lifecycle and event delivery remain unfinished acceptance criteria. Invitations requiring a lower active OWNER role are intentionally refused rather than silently granting a different role from the token or removing ownership through onboarding.

## Demo transaction participation

The invitation store is registered as an Organization-only transaction
participant. Both the account/Organization and Work gates must be held when it
is captured/restored; ordinary Work snapshots must not restore invitation state
over concurrent identity operations. Its immutable record dictionaries and Portal
grant set are restored alongside Organization membership, Board membership and
Work events before the command releases those gates.

The new API-host fixtures inject refusal after actual invitation insertion,
require no retained invitation/history/creation receipt, then require a successful
same-key retry and immutable replay without another publication. They cover both
Organization and Board invitations with actor refusal, exceptions and cancellation.
Three acceptance cases withdraw actor admission only after the real store consumes
the token and grants access; they require token, membership/Portal/Board grants and
event state to be restored, followed by successful acceptance and single-use
rejection. The publication callback is a fixture, not mail delivery evidence.

Final compilation passed with zero warnings/errors. Native execution remains
required in CI. Identity-owned invitation-backed registration and trusted direct
store calls retain their own transaction boundaries and need separate rollback
proof; this change does not establish universal Demo or durable provider integrity.
