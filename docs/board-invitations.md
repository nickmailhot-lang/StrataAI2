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

Board-target publication, discovery, recipient proof review and acceptance remain
unavailable while consumers are upgraded; mail snapshots missing the exact
canonical Board target are unusable instead of sending an ordinary Organization
envelope. Migration 026 binds optional Board ID/role on protected mail snapshots
to the real tenant Board. The narrow Worker capability admits a matching active
Board target only with current Organization administration, or current Board
administration and an existing eligible recipient Organization member. It checks
parent lifecycle, issuer membership, recipient account/verification policy and
exact target-role equality without granting broad identity/membership reads.
Required PostgreSQL fixtures verify target integrity,
accepted-role immutability, snapshot mismatch, Board archive, issuer removal or
demotion, and recipient suspension, verification or membership removal. A host case verifies that a
staged Board target cannot produce an Organization grant or registration proof.

`BoardInvitationService.CreateAsync` now consumes this policy and target storage
inside the existing Organization command transaction. It re-reads the real Board
after its command lock, checks current issuer/recipient eligibility before retry
replay, reserves a purpose-specific creation fingerprint, and writes one invitation,
audit event, content-free `BOARD_MEMBER_INVITED` Board invalidation/outbox and receipt.
Creation grants no access or account. Keyed acknowledgments never contain a bearer;
changed target/input conflicts and revoked administrators cannot replay old receipts.
Configured mail publication remains unavailable until Board mail snapshots are
integrated, and the command has no public endpoint at this stage. Demo host cases
exercise both Board roles, concurrent retry/event deduplication, onboarding authority,
removed memberships, invalid input and archive behavior. PostgreSQL atomic command
and exact-image execution of this new command still require the endpoint integration.

Board signup proof now admits a current Organization Owner/Admin-issued Board
target for the intended email. PostgreSQL freezes the actual parent, issuer
membership, active same-tenant Board, invitation and global accounts in that order,
then rechecks target shape/lifecycle and current enrollment authority before and
after registration writes. Signup creates only the account and verification intent;
verification alone grants neither Organization nor Board access and does not consume
the invitation. Board administration alone cannot enroll a new Organization member.
Five host cases cover both Board roles, email binding, retry acknowledgment,
verification without grants, archive, revocation and issuer demotion. The mandatory
exact-image signup fixture now covers both Board roles, audit rollback, stable retry,
no early Board grants, archived replay denial and an actual Board archive committed
during the signup Board-lock wait. That fixture attaches the canonical target with
disposable administrative SQL to test this consumer; it does not prove public Board
invitation creation or actual Board invitation-mail delivery. Execution evidence
for this increment is pending CI.

These increments contain policy, persistence, creation and signup-proof support. They are not wired
to a public Board invitation creation endpoint or recipient preview,
acceptance, or Worker delivery. Existing Organization/Portal behavior is unchanged.
The next integration must consume the tenant-bound Board target and add immutable mail
snapshots; enforce the same current authority in creation, discovery, proof review,
signup and acceptance; atomically grant Board membership with audit/events and retry
receipts; and provide MUI review with desktop/mobile keyboard and exact-image CI
coverage. Board-target invitations must remain unavailable until these consumers
agree. Neither PRD is complete or eligible for closure at this stage.
