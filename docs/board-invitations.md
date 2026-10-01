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

The persistence foundation adds optional Board ID/role to the canonical invitation
through migration 025. Its composite foreign key binds the Board to the actual
Organization; shape constraints prohibit Portal Board targets, partial targets,
invalid Board roles and Organization OWNER/ADMIN enrollment through this target.
Routing copies both fields, and accepted target attribution is immutable. The
PostgreSQL adapter round-trips the target, including creation retry receipts.

Board-target publication, discovery, proof review, signup and acceptance remain
unavailable while consumers are upgraded; the existing mail capability marks a
canonical Board-target invitation unusable instead of sending an ordinary
Organization envelope. Required PostgreSQL fixtures verify target integrity,
accepted-role immutability and this mail boundary. A host case verifies that a
staged Board target cannot produce an Organization grant or registration proof.

These increments contain policy and persistence foundations. They are not wired
to a public endpoint, recipient preview, signup proof,
acceptance, or Worker delivery. Existing Organization/Portal behavior is unchanged.
The next integration must consume the tenant-bound Board target and add immutable mail
snapshots; enforce the same current authority in creation, discovery, proof review,
signup and acceptance; atomically grant Board membership with audit/events and retry
receipts; and provide MUI review with desktop/mobile keyboard and exact-image CI
coverage. Board-target invitations must remain unavailable until these consumers
agree. Neither PRD is complete or eligible for closure at this stage.
