# Board invitations — permission foundation

PRD-05 PERM-FR-004 and PRD-60 ONBOARD-FR-001/004 require Board invitations in
addition to the implemented Organization and Portal invitation flows.

`BoardInvitationPolicy` records the permission decision for the next integration:
an active Board administrator with active membership in the same Organization can
invite an existing eligible Organization member as Board MEMBER or ADMIN. Public
visibility grants no invitation authority. Only a current Organization Owner/Admin
can issue a Board invitation that enrolls an outsider into the Organization. Such
enrollment grants Organization MEMBER, preserving an existing higher Organization
role. Board administration alone cannot expand Organization membership.

Both parent records must be active. Issuer account, Organization membership, Board
membership when required, recipient eligibility, and exact tenant/actor identifiers
must be evaluated from current records after acquiring command locks. Verification
follows the configured identity policy; disabling email verification never permits
pending, suspended or deactivated accounts. A policy result is not a durable grant
and cannot replace current session authorization or admission after a lock wait.

This increment contains the policy and its regression cases only. It is not wired
to a public endpoint, persisted Board invitation, recipient preview, signup proof,
acceptance, or Worker delivery. Existing Organization/Portal behavior is unchanged.
The next integration must add tenant-bound Board targets and immutable mail
snapshots; enforce the same current authority in creation, discovery, proof review,
signup and acceptance; atomically grant Board membership with audit/events and retry
receipts; and provide MUI review with desktop/mobile keyboard and exact-image CI
coverage. Board-target invitations must remain unavailable until these consumers
agree. Neither PRD is complete or eligible for closure at this stage.
