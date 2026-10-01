# Board administrator continuity

PRD-05 PERM-FR-005/006 uses the same last-explicit-Board-admin safeguard for
membership removal and role demotion. A Board admin whose Organization role is
Member cannot demote the last active explicit ADMIN to MEMBER. Rejection uses
`sole_board_admin` and leaves membership/version, audit, event, outbox and keyed
command state unchanged. Concurrent ordinary admin self-demotions serialize
through the existing Board command scope and must leave one explicit admin.

The existing Organization Owner/Admin override remains available for both
removal and demotion. These actors retain administrative Board access through
their current Organization role even without explicit Board membership. The
override remains transactional with audit/events/retry state, and a failed audit
write must roll back the role change. This is the existing policy, not a new
guest or Organization-wide sharing feature.

Two API host regressions cover sole-admin keyed rejection, unchanged membership,
Owner recovery and concurrent self-demotions. The required exact-image fixture
uses restricted PostgreSQL API credentials, concurrent real requests, full
command-state comparison and forced audit failure. Local warnings-as-errors
build and shell/configuration checks pass; new CI execution remains pending.
Board invitation grants and the rest of PRD-05/60 acceptance remain incomplete.
