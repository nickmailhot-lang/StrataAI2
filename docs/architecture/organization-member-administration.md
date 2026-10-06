# Organization member administration

The Organization home links current internal Owners and Admins to `/app/{organizationId}/members`. The MUI screen displays bounded UUID-cursor pages, canonical roles, account lifecycle, and usable-owner eligibility. Previous/next navigation reloads current authorized pages. Pending reads, malformed contracts and permission loss hide private directory rows; session loss returns to sign-in.

Removal starts with a current, authorized single-member review. `GET /organizations/{organizationId}/members/{userId}` returns `{organizationId, member, actorRole}`; `member: null` means there is currently no active internal membership, only after the actor is authorized. Directory pages also return the current actor role. Portal-only access, ordinary Members, foreign Organizations, inactive Organizations and revoked sessions cannot obtain either contract. The query uses the same parent/member/account/session transaction and restricted tenant connection as paginated reads. It returns no additional global identity fields and creates no audit mutation.

The confirmation dialog shows the freshly reviewed person and role, focuses Cancel, and warns about loss of the actor's own access when applicable. Confirm sends the exact reviewed membership version. Current server authorization and the usable-owner floor remain authoritative. A fresh Admin cannot confirm removal of an Owner. A sole-owner denial explains the continuity requirement.

After a definitive refusal, the screen clears private details and requires a new
exact-member review and explicit confirmation. After a timeout or missing
acknowledgment, it holds the original UUID, reviewed account, target and version.
Load/review and other removals are blocked while that receipt is unresolved;
Retry original removal sends the same DELETE path and key. Only 204 acknowledges
the command. A replay notice identifies the original removal and requires current
membership review before considering a later change, so a rejoined member is
not removed again or reported as currently absent. Access refusal clears private
state and pending intent; a known member_not_found refusal permits authorized
current-state reconciliation without inventing a successful acknowledgment.
The shared Problem boundary recognizes that code but discards server detail.

Cancel remains the dialog's initial focus. Uncertain removal returns focus to the
original-retry button; a keyboard retry that resolves returns focus to its
acknowledgment unless the user chose another focus target. Current state is read
separately after the original receipt is acknowledged. Route changes retire local
pending recovery; no automatic DELETE or background retry is sent.

Each request is bounded to 15 seconds even when the transport ignores abort. Pending requests reject duplicate activation; keyed routes and unmount fences reject late results. Closed-dialog recovery returns keyboard focus to review/reload. Member cards wrap long text on narrow screens.

Local validation: web typecheck and zero-warning lint, the full component suite, warnings-as-errors solution build, and desktop/mobile keyboard browser scenarios. Browser scenarios use actual accepted invitations, cancel safely, commit removal while dropping its acknowledgment, verify no second write, and confirm revoked access/current exact absence. Required exact-image CI extends one-connection PostgreSQL coverage to exact lookup, Portal/tenant denial, stale removal consent, and post-wait role/session revocation. These database/release execution results are pending until their CI logs pass.

The [invitation creation form](invitation-administration-ui.md) is now available. Remaining PRD-03 requirements include invitation delivery/history/revocation, member leave/Organization deletion confirmation, deletion/retention processing, governance/ownership management, remaining durable receipts and Organization realtime events. This change does not close PRD-03 or the architecture tickets.


Focused source cases cover original success/cancellation, current review/version
conflict, lost/repeated responses, expiry, account/access refusal, later rejoin,
keyboard recovery focus, definitive absence, late-route/deadline fencing and
bounded directory pages. Desktop/phone native coverage commits removal while
losing the response, restores actual membership through invitation acceptance,
replays identical account-bound path/key and checks unchanged rejoined membership
and renewed keyboard review. Native execution remains pending CI.

All 19 focused member-screen cases and 12 shared Problem-boundary cases pass.
Web/browser TypeScript and zero-warning lint pass. Native coverage also includes
a WCAG 2.2 AA automated check of the confirmation; actual execution is pending CI.

## Live membership reconciliation

The screen subscribes to the production Organization stream after validating its
account and directory admission. Stream changes, reset and unavailability clear
cached member names and any unconfirmed removal dialog immediately. Reads that
began before that invalidation cannot restore stale consent. A queued first-page
refresh waits for the current request to finish and checks the account before
and after reading; review and removal use the same identity checks. Account
replacement or confirmed access refusal clears private state and recovery.

An original uncertain removal retains its exact account, target, version and
idempotency key. Live reconciliation can recheck current administrative access
while that intent is unresolved, but does not show new member rows or permit a
new confirmation. It does not send another DELETE automatically. The explicit
original retry continues to recover that command's acknowledgment, with later
membership review kept separate. An in-flight removal is allowed to finish;
live invalidation does not discard its result or substitute a new request.

All 26 focused member-screen cases pass locally, including live consent
retirement, late-review fencing, live access refusal, account replacement,
lost acknowledgment and live reconciliation during an in-flight removal.
Web and browser TypeScript and zero-warning lint pass. New mandatory desktop
and phone browser scenarios use ordinary invitations, actual release Worker
delivery and observed SignalR member additions, retire an open confirmation,
lose a committed removal response and then replay its exact key after actual
membership restoration. They also check attribution, accessibility and narrow
viewport layout. Their runtime execution remains pending CI. Member removal,
departure and invitation source integration and Demo event parity remain
unfinished; this consumer does not complete PRD-03.
