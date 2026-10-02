# Bounded PostgreSQL routing discovery (ARCH-04)

Migration 027 enables and forces RLS on all five routing tables: Board/List/Card
routes, user Organization access and invitation routes. These tables retain
non-null Organization keys. Missing routing and tenant context returns no rows.
Tenant context admits only matching rows and is the only context that permits
route writes, including existing synchronization triggers.

Discovery before selecting a tenant uses a typed, parameterized transaction-local
lookup kind/key. Entity and invitation-ID/token lookups select one matching
route. Organization discovery selects the requested user's routes; verified-email
invitation discovery selects the authorized recipient's routes. Registration
proof uses the token lookup, not recipient-wide discovery. The existing application
checks still authorize the user, verified email, token and current grants; finding
a route does not authorize protected data or grant membership.

Each lookup replaces the previous kind/key. Standalone routing reads now own a
short transaction, which is disposed with their connection. Reads inside existing
identity or tenant commands borrow the owning transaction, preserving atomicity,
one-connection operation and lock ordering. Routing context is transaction-local
and does not survive commit/rollback or a pooled connection return. No session-wide
tenant or route settings are introduced.

Runtime schema compatibility now requires migration 027. The forward-upgrade
fixture applies it repeatedly after the populated 26-version schema and verifies
all five forced-RLS flags. Exact-image compatibility tests remove/restore its
ledger entry alongside existing baselines, requiring refusal and recovery.

The required real PostgreSQL fixture uses actual restricted API login sessions.
It checks every routing table with omitted WHERE predicates, foreign-tenant
predicates, missing/wrong/malformed lookup context and the full supported lookup
kinds. Discovery cannot update or insert route metadata. Committed read context
must disappear. Existing canonical membership trigger, deferred-integrity and
rollback tests retain their assertions under explicit tenant write context.
The general migrated-catalog guard now has no routing exemptions.

The solution builds with zero warnings/errors and shell syntax/diff checks pass.
Linux execution of the new PostgreSQL checks and exact-image regression suites
is required before this change establishes runtime or release evidence.
