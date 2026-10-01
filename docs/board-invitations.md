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

Board-target publication, discovery and recipient proof review remain
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
missing Board target cannot produce an Organization grant or registration proof.

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

Explicit acceptance now supports a verified matching recipient through body proof
or natural invitation ID. The Organization root locks current issuer membership and
the actual Board before re-reading the unchanged canonical target. Current Board
policy runs again before grants. PostgreSQL atomically consumes the invitation,
enrolls Organization MEMBER where authorized, grants the target Board role, and
writes audit/Board events/outbox. Existing active Organization roles and active Board
ADMIN grants are preserved; privileges attached to a removed Organization membership
are not revived. `BOARD_MEMBER_ADDED`/`BOARD_MEMBER_ROLE_CHANGED` fire only for the
actual membership change, with a content-free `INVITATION_ACCEPTED` invalidation.

The acceptance acknowledgment includes the bound Board ID/role and no bearer.
Body proof is single use; natural-ID retry acknowledges the original acceptance
without restoring later-removed Board membership or repeating events. Ten host
cases exercise both roles, actual authenticated body/natural-ID routes, preservation,
removed historical roles, archive and current issuer/recipient revocation. Required
exact-image cases cover Board/Organization grant, consume, audit/event/outbox rollback,
role preservation, single use and retry after membership removal. These consumer
fixtures use administratively attached targets; public issuance, actual Board mail,
recipient discovery and public issuance remain separate integration requirements. The
acceptance cases passed in run 36903987812; the full run failed at a later discovery
fixture that still expected FOR UPDATE although canonical natural-ID lookup now
blocks earlier at FOR SHARE. The fixture is corrected without weakening expiry
or rollback assertions. Demo storage retains its documented lack
of cross-store rollback; production atomicity evidence must come from PostgreSQL.

Recipient proof review now locks the actual Board before canonical lookup, requires
an active verified matching account, and rechecks the same current issuance policy.
It returns the current Board name and bound ID/role without granting or consuming.
Both role host cases check authenticated review, email binding and no early grant;
archive and issuer/recipient revocation cases now also deny review.

MUI displays the Organization, current Board name and Board role before separate
acceptance. It rejects malformed or Portal Board metadata, binds acknowledgment to
the exact reviewed Board and role, and preserves the same natural ID for a lost
acknowledgment. Successful acknowledgment links to the actual Board route. Nine
new component cases exercise target binding, recovery and malformed metadata.
The required exact-image signup fixture also checks authorized review and unchanged
grant/consume/event state. Execution of these new review cases is pending CI.

These increments are not wired to public Board invitation creation or Worker mail
publication. Recipient discovery, desktop/mobile keyboard browser coverage and
actual Board invitation delivery remain required. Board issuance must stay disabled
until every consumer agrees. Neither PRD is eligible for closure.
