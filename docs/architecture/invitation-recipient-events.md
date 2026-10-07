# Private invitation recipient source

Return to the [documentation index](../README.md) or
[recipient discovery](invitation-discovery.md).

Migration `102_invitation_recipient_events` creates a durable recipient source
for the remaining PRD-03 / PRD-60 live invitation workflow. This source is an
explicit cross-Organization routing relationship, like existing verified-email
invitation discovery. It does not grant Organization, Board or Portal access and
does not widen the Internal Organization or Board streams. The Production
recipient replay reader is implemented; recipient transport, Demo source
parity and browser consumer are not yet implemented.

## Source and atomicity

Future actual invitation creation, acceptance and revocation transitions capture
an immutable proof of the canonical Invitation revision. Creation begins at
revision one. Acceptance requires a new accepting actor and acceptance timestamp;
revocation requires a new revocation timestamp on an unaccepted pending row.
The recipient, surface, target role/Board and issuer must remain unchanged across
these lifecycle transitions. No-op updates and repeated receipt acknowledgments
do not capture another transition. Legacy state/audits are not backfilled.

The corresponding canonical audit insertion publishes one source event. It
retains the original audit ID, source event type, actual authenticated actor,
Organization, Invitation ID/revision, correlation and canonical transition
timestamp. Creation audits normalize to `INVITATION_CREATED`; accepted/revoked
audits retain `INVITATION_ACCEPTED` / `INVITATION_REVOKED`. Internal, Board and
Portal source records retain their separate target surface in private proofs.
Accepted sources require the appropriate persisted Internal, Board or Portal
grant; Portal-only grants cannot manufacture Internal membership.

Source projection checks the active parent/account, actual transition proof and
applicable current administrative authority for creation/revocation. These
checks supplement the existing owning command/session policy. They are not an
alternative authorization API. The journal, proof, source audit, routing row,
grant/command and recipient sequence are in the same existing PostgreSQL command
transaction. A late projection/receipt/final-actor failure rolls everything back,
including the sequence increment. One Invitation revision cannot publish twice.

## Data and capability contract

| Table | Keys and ownership | Lifecycle and capability |
| --- | --- | --- |
| `invitation_recipient_proofs` | Invitation/Organization/revision primary key; tenant-safe Invitation and optional Board FKs; issuer/accepting User FKs | Future transition proof; published proofs remain immutable and retained; unpublished proofs can retire only with their parent Invitation; forced tenant RLS; neither runtime role can read or write directly |
| `invitation_recipient_streams` | Normalized recipient email primary key; monotonically increasing committed sequence across Organizations | Private global recipient routing counter; forced recipient RLS; API can read its routing counter only; no runtime writes |
| `invitation_recipient_events` | Audit ID primary key; unique recipient/sequence and Invitation/Organization/revision; FKs to immutable proof, audit, Organization, users and recipient stream | Immutable committed journal; forced recipient RLS; API can read only recipient key, event ID/type, sequence and timestamp; Worker has no raw access |

Normalized email is private routing data, never analytics or a public stream
name. The only lookup policy is `INVITATION_RECIPIENT` with the server-selected
normalized email. Invitation-ID routing and foreign recipient lookup return no
rows. API column grants exclude Organization/Invitation/actor IDs, target grants,
correlation and other domain references. Direct proof access, counter writes and
projection-function calls are denied. The existing recipient service must bind
reads to the current verified account before selecting this lookup; the
database routing capability alone is not proof of an authenticated session.

Metadata is exactly `{}`. The journal stores no names, descriptions, bodies,
raw tokens, token hashes or links. References and timestamps preserve historical
meaning; ordinary deletion cannot erase published proofs or history. Migration
`103_invitation_recipient_unpublished_cleanup` permits the parent Invitation's
referential cascade to remove a proof only when that revision has no journal
event. Standalone proof deletion and all proof updates remain forbidden. This
allows unaudited fixture cleanup without relaxing published source retention.
Approved retention
and physical provider/backup purge remain separate policy requirements.

This synchronous source projection creates no external work or new deployable
service. Existing separate Worker mail and Organization/Board delivery remain
unchanged. Global recipient notifications will consume these durable source
identities rather than expose the tenant journals to nonmembers.

## Account-bound replay

Production registers `TransactionalInvitationRecipientSynchronization` around
the existing owning identity observation transaction. It holds shared account
admission and verifies the original request actor and persisted session before
and after replay. The PostgreSQL reader refuses calls outside the owning account
scope, requires an active verified account and derives the normalized recipient
email from that account. Callers cannot supply a recipient email.

Protected cursors bind the actor, normalized email, account revision and sequence
with a distinct Data Protection purpose and a 15-minute expiry. The private
binding is not an event payload. Bootstrap, invalid/expired bindings, a cursor
ahead of the committed head, or missing/noncontiguous source history return an
empty reset page at the current head. Consumers must reload protected invitation
discovery on reset. Bounded replay reads at most 101 rows for a requested page
of 1–100 events, without stepping beyond the captured committed head.

The outbound event has only `eventId`, normalized `eventType`, `sequence` and
`createdAt`. Organization/Invitation IDs, issuer, recipient email, roles and
correlation remain private source data. Final account/email/revision or session
withdrawal discards the whole page. A separate current-cursor check lets future
transport reauthorize after session I/O. Recipient events invalidate discovery;
they do not confer continuing Organization, Board or Portal admission.

No endpoint or hub currently exposes this reader. Demo registration waits for
transactional source parity rather than returning a fabricated empty journal.

## Verification and remaining delivery

The mandatory SQL fixture covers Internal/Portal/Board creation, all three
acceptance surfaces, revocation, eight contiguous events across two Organizations,
exact source attribution and revision, duplicate publication refusal, immutable
history, published-parent deletion refusal, standalone unpublished-proof deletion
refusal, unpublished-parent cleanup without a sequence change, missing
Portal-grant refusal, complete late-failure rollback, recipient RLS, private column/proof
refusals and Worker isolation. It uses restricted `SET ROLE` permission checks;
this is not real-cookie/session authorization or browser delivery evidence.

The source is included in runtime schema readiness and the forward-upgrade /
repeat / serialization / failed-migration suite. The exact-image creation and
acceptance fixtures now compare recipient proof/journal/counter state during
refusal and rollback, require canonical publication and no duplicate on receipt
replay. Those exact-image assertions still require runtime execution.

Remaining implementation includes protected SignalR recipient transport, Demo
transactional source parity, browser bootstrap and missed-event recovery,
client invalidation/accessible announcements and
explicit original acceptance recovery. Parent/issuer authority changes and
account/email changes require current protected discovery rather than assuming a
creation event confers continuing access. Native two-client, disconnect, cookie
withdrawal, Portal separation, keyboard/mobile and latency evidence remain
required. This source does not complete PRD-03 or PRD-60.

Local validation passed the complete source SQL fixture against a fresh
103-migration PostgreSQL/pgvector database, restricted role provisioning and the
schema isolation catalog check. The original restricted routing-isolation
fixture also passed, including its unaudited Invitation cleanup. This repairs
the cleanup failure reported by CI for commit `5f3e0f8`; current CI still needs
to verify the forward migration. The Linux migration runner passed clean/repeat,
forward upgrade with no invented history, concurrent runner serialization and
failed/unrecorded migration rollback. The full .NET Release solution build passed
with zero warnings/errors; affected release scripts pass Bash syntax checks.
These source/persistence results do not certify real sessions, Demo source
parity, recipient SignalR/browser delivery or current exact-image CI acceptance.

Reader validation adds domain checks for contiguous bounded replay, missing or
duplicate identities, fixed event types, cursor tampering/expiry and actor/email/
revision binding, final account withdrawal and content-free payloads. A mandatory
C# persistence contract uses the real restricted API login and persisted session
checks for cross-Organization Internal/Portal source order, original audit IDs,
later actual revocation recovery, no duplicate replay, no conferred membership,
unowned read refusal, switched request actor, changed email/revision, unverified
account and session revocation. Its request context is synthetic: it is not a
cookie, SignalR or browser acceptance test. `--invitation-recipient-only` runs
this contract in isolation for diagnosis; the normal CI persistence executable
always runs it before the remaining contracts.
