# Private invitation recipient source

Return to the [documentation index](../README.md) or
[recipient discovery](invitation-discovery.md).

Migration `102_invitation_recipient_events` creates a durable recipient source
for the remaining PRD-03 / PRD-60 live invitation workflow. This source is an
explicit cross-Organization routing relationship, like existing verified-email
invitation discovery. It does not grant Organization, Board or Portal access and
does not widen the Internal Organization or Board streams. The recipient
transport and browser consumer are not yet implemented.

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
| `invitation_recipient_proofs` | Invitation/Organization/revision primary key; tenant-safe Invitation and optional Board FKs; issuer/accepting User FKs | Future transition proof; immutable and retained; forced tenant RLS; neither runtime role can read or write directly |
| `invitation_recipient_streams` | Normalized recipient email primary key; monotonically increasing committed sequence across Organizations | Private global recipient routing counter; forced recipient RLS; API can read its routing counter only; no runtime writes |
| `invitation_recipient_events` | Audit ID primary key; unique recipient/sequence and Invitation/Organization/revision; FKs to immutable proof, audit, Organization, users and recipient stream | Immutable committed journal; forced recipient RLS; API can read only recipient key, event ID/type, sequence and timestamp; Worker has no raw access |

Normalized email is private routing data, never analytics or a public stream
name. The only lookup policy is `INVITATION_RECIPIENT` with the server-selected
normalized email. Invitation-ID routing and foreign recipient lookup return no
rows. API column grants exclude Organization/Invitation/actor IDs, target grants,
correlation and other domain references. Direct proof access, counter writes and
projection-function calls are denied. The existing recipient service must bind
future reads to the current verified account before selecting this lookup; the
database routing capability alone is not proof of an authenticated session.

Metadata is exactly `{}`. The journal stores no names, descriptions, bodies,
raw tokens, token hashes or links. References and timestamps preserve historical
meaning; ordinary deletion cannot erase proofs or history. Approved retention
and physical provider/backup purge remain separate policy requirements.

This synchronous source projection creates no external work or new deployable
service. Existing separate Worker mail and Organization/Board delivery remain
unchanged. Global recipient notifications will consume these durable source
identities rather than expose the tenant journals to nonmembers.

## Verification and remaining delivery

The mandatory SQL fixture covers Internal/Portal/Board creation, all three
acceptance surfaces, revocation, eight contiguous events across two Organizations,
exact source attribution and revision, duplicate publication refusal, immutable
history, missing Portal-grant refusal, complete late-failure rollback, recipient RLS, private column/proof
refusals and Worker isolation. It uses restricted `SET ROLE` permission checks;
this is not real-cookie/session authorization or browser delivery evidence.

The source is included in runtime schema readiness and the forward-upgrade /
repeat / serialization / failed-migration suite. The exact-image creation and
acceptance fixtures now compare recipient proof/journal/counter state during
refusal and rollback, require canonical publication and no duplicate on receipt
replay. Those exact-image assertions still require runtime execution.

Remaining implementation includes the owning verified-account replay reader,
actor/email-bound cursor, protected SignalR recipient transport, bounded bootstrap
and missed-event recovery, client invalidation/accessible announcements and
explicit original acceptance recovery. Parent/issuer authority changes and
account/email changes require current protected discovery rather than assuming a
creation event confers continuing access. Native two-client, disconnect, cookie
withdrawal, Portal separation, keyboard/mobile and latency evidence remain
required. This source does not complete PRD-03 or PRD-60.

Local validation passed the complete source SQL fixture against a fresh
102-migration PostgreSQL/pgvector database, restricted role provisioning and the
schema isolation catalog check. The Linux migration runner passed clean/repeat,
forward upgrade with no invented history, concurrent runner serialization and
failed/unrecorded migration rollback. The full .NET Release solution build passed
with zero warnings/errors; affected release scripts pass Bash syntax checks.
These source/persistence results do not certify real sessions, Demo source
parity, recipient SignalR/browser delivery or current exact-image CI acceptance.
