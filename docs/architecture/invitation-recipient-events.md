# Private invitation recipient source

Return to the [documentation index](../README.md) or
[recipient discovery](invitation-discovery.md).

Migration `102_invitation_recipient_events` creates a durable recipient source
for the remaining PRD-03 / PRD-60 live invitation workflow. This source is an
explicit cross-Organization routing relationship, like existing verified-email
invitation discovery. It does not grant Organization, Board or Portal access and
does not widen the Internal Organization or Board streams. Both runtime modes
implement account-bound recipient replay and protected SignalR transport. The
invitations page consumes it for actual invitation transitions; broader authority
invalidation and native acceptance evidence remain incomplete.

## Executed edge transport repair

Release CI run `37674465017` failed all four Demo recipient lifecycle browser
cases before their initial live reset. The unchanged desktop connected case
reproduced locally: invitation discovery worked, but the live feed reported
interruption. Nginx had upgrade locations for the other feeds and omitted
`/invitations/live`, sending its WebSocket handshake through the ordinary HTTP
proxy. The dedicated priority location now forwards the upgrade/connection,
host and scheme headers and disables buffering, with the same timeout as the
other authorized feeds. No hub admission or browser assertion changes.

After the repair, all four unchanged desktop/phone connected/disconnected cases
pass together in a two-minute invocation including required rate pacing. They
prove genuine initial private reset envelopes, consent withdrawal before a
held recovery read, focus, inaccessible Organization scope for Portal-only
recipients, terminal deletion completion, original deletion receipt recovery,
body-free live envelopes, WCAG checks and overflow. The local stack uses frozen
compiled API/web artifacts in cached runtimes with current Nginx, not retained
current-release image proof. Its two temporary containers and network were
removed, preserving the original three active services and all volumes.

`scripts/ci/verify-realtime-proxy.py` compares the declared API hubs with their
priority edge locations and required upgrade/forwarding/unbuffered directives.
It rejects the historical missing invitation location and passes for all eight
current hubs. This is mandatory before source quality permits image builds;
the unchanged browser scenarios remain mandatory against the built-once images.

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

Migration `104_invitation_recipient_retained_board_admin` aligns Board source
publication with the existing acceptance policy: a Member-target invitation can
acknowledge a retained active Board Admin grant. An Admin-target invitation still
requires Admin, and every Board acceptance still requires active Board and
Organization membership. This preserves the canonical grant without demanding
an artificial downgrade. Internal and Portal grant checks remain unchanged.

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

Protected cursors bind the actor, normalized email, account revision, private
authority revision and sequence with a distinct version-2 Data Protection
purpose and a 15-minute expiry. Production reads the authority revision inside
the owning account transaction; an absent counter is zero. Version-1 cursors
reset after deployment. Demo simulates delivery in fixed 100-candidate indexed
pages inside its owning Organization command, with rollback-safe private
checkpoints, recipient deduplication and revision effects. Both readers consume
the current private revision. See
[authority delivery and binding](invitation-recipient-authority.md#production-protected-cursor-binding).
The private
binding is not an event payload. Bootstrap, invalid/expired bindings, a cursor
ahead of the committed head, or missing/noncontiguous source history return an
empty reset page at the current head. Consumers must reload protected invitation
discovery on reset. Bounded replay reads at most 101 rows for a requested page
of 1–100 events, without stepping beyond the captured committed head.

The outbound event has only `eventId`, normalized `eventType`, `sequence` and
`createdAt`. The sequence is a canonical decimal string on the JSON wire, so
JavaScript preserves every signed 64-bit position rather than rounding it.
Organization/Invitation IDs, issuer, recipient email, roles and
correlation remain private source data. Final account/email/revision or session
withdrawal discards the whole page. A separate current-cursor check lets the
transport reauthorize after session I/O. Recipient events invalidate discovery;
they do not confer continuing Organization, Board or Portal admission.

## Protected recipient transport

Both modes expose the authenticated `/invitations/live` SignalR hub. `Watch`
accepts only the opaque cursor or `null`; callers cannot select another account,
recipient email, Organization or Board. The existing strict trusted-origin
middleware also protects this route. One subscription per connection, existing
4-KiB incoming-message limits and one buffered stream item bound resource use.

The hub captures the original session cookie, requires an active verified account
and uses the owning transactional reader. Each page is followed by fresh session
authentication and a current protected-cursor check before delivery. Withdrawn
session/account admission aborts the connection without delivering that page.
Cancellation releases the subscription slot. Reads use the durable source every
second, drain additional bounded pages immediately and emit a neutral heartbeat
after 20 otherwise empty polls. Reconnection with the original protected cursor
replays committed missed transitions; invalid or expired bindings require an
empty reset and current protected discovery.

The browser supplies its preconnection reviewed account as `expectedActorId` in
the connection URL. The hub treats it only as an admission precondition: a
mismatched, empty, malformed or duplicate value aborts before any source read or
bootstrap delivery. The actual original session still chooses the actor and
recipient scope; the query cannot substitute that scope. Each reconnect retains
the original reviewed account. Callers omitting this optional precondition keep
the session-only behavior for existing API clients.

The browser consumer below owns protected discovery and acceptance recovery.
Parent/issuer authority invalidation remains a separate unfinished dependency.
Its [bounded Worker dispatch contract and remaining storage work](invitation-recipient-authority.md)
are tracked separately from actual invitation transitions.

## Browser transport boundary

`invitationRecipientLive.ts` provides the cookie-authenticated WebSocket adapter
consumed by the invitations page. It resumes the exact last admitted
opaque cursor, fences late callbacks from a prior connection generation and
backs off failed connection attempts. Initial/expired-cursor reset and actual
transitions synchronously invalidate the consumer's consent. Reconnecting or
unavailable delivery also invalidates immediately. Empty heartbeats can rotate a
protected token without manufacturing a change notification. Cleanup cancels
pending retries and subscriptions, including a late successful connection.

`invitationRecipientSync.ts` accepts only the four public page fields and four
public event fields. It refuses extra private fields, malformed dates/tokens,
noncanonical/out-of-range sequence strings, duplicate source identities and
noncontiguous pages. It uses `bigint` for sequence checks. An opaque bootstrap
head has no client-readable sequence; continuity starts at the first actual
event after reset. Full pages remain bounded to 50 events, and resets must be
empty. This validation cannot independently decode the account binding; the hub
owns that binding and the page consumer freshly verifies its reviewed
account around protected discovery and acceptance.

Thirty-five browser unit cases passed for this contract and adapter, including
positions above JavaScript's safe integer range through `long.MaxValue`, private
payload refusal before cursor advancement, stale callbacks, exact resume,
heartbeat rotation, reset, retry backoff and cleanup. Two domain serialization
cases additionally verify the actual server JSON at those large positions; all
nine actual Demo socket cases pass with decimal-string sequences. These checks
do not establish native browser consumption, accessible announcements or current
exact-image end-to-end acceptance.

## Protected page recovery

The page captures `/me` before opening the stream, binds the connection to that
original account, then waits for the captured stream head before initial
protected discovery. Its first listing retains that account's `expectedActorId`
and requires final account confirmation. Account capture, stream bootstrap and
the first protected page share the original 15-second deadline; elapsed account
capture is not added back as a fresh page allowance. Manual Refresh cannot
bypass an unfinished bootstrap.
A missing bootstrap is bounded to 15 seconds and enables ordinary protected
HTTP recovery. Repeated transport failure callbacks coalesce so they cannot
continually interrupt that recovery. Resets, actual transitions and reconnects
withdraw old invitation labels, consent and acknowledgment links, abort obsolete
reads and queue a current first-page discovery. The original reviewed account
is checked around each read and each explicit command. A replacement account
withdraws all display/recovery and returns to sign-in. No stream event directly
grants Organization, Board or Portal admission.

An event before a command is submitted withdraws that unsent consent. An event
interrupting a submitted command keeps its original ID/acknowledgment fields in
the generic recovery panel. Late responses cannot confirm it. Fresh protected
discovery must finish before explicit same-ID retry; an empty pending page does
not acknowledge acceptance and automatic recovery never POSTs. A live event can
therefore arrive before a successful mutation response and require explicit
confirmation of that original attempt.

Changes use a polite, atomic live status without cached private labels. Focus is
preserved on existing controls; if a withdrawn private invitation or acceptance
link owned focus, it moves to the stable Refresh control. Refresh remains
focusable with `aria-disabled` while a request is active; the existing in-flight
guard refuses another request.

All 88 affected component/adapter/contract cases pass, including seven page-live
cases for bootstrap order, stale-read fencing, unsent/submitted acceptance,
replacement accounts, bounded bootstrap/coalescing, cleanup and focus. Typecheck,
lint and browser fixture typecheck pass. Two new desktop/mobile native cases use
actual issuer/recipient API sessions, future Portal creation/revocation,
disconnect-created source recovery, neutral socket fields, nonmember isolation,
focus, accessibility and logout. They are collected but await exact-image
execution. The aggregate HTTP deadline fixture explicitly closes this transport
to isolate its deadline from earlier live invalidation.

These changes cover future actual invitation transitions. Parent deletion,
issuer role/access withdrawal, current names and other authority changes are not
represented by this recipient source yet; protected discovery remains authoritative,
but automatic invalidation for those dependencies and native concurrency/latency
evidence must still be implemented or proven before ticket closure.

The account-startup follow-up passes 90 affected browser cases and all 15 actual
Demo WebSocket/session cases. Six new socket cases reject substituted, zero,
malformed, empty or duplicate reviewed actors and admit the matching account
without exposing its private binding. Two page cases verify the original actor
on first discovery and bounded initial profile JSON without a late stream/read.
The final focusable Refresh bootstrap guard also passes all nine page-live
cases. Release build, TypeScript, lint and browser typecheck pass. Two additional
desktop/mobile native cases replace the actual cookie after capturing the real
original profile response and require no source delivery or invitation read.
They are collected, with exact-image runtime still pending.

If initial account admission itself fails, explicit Refresh starts another bounded
account-admission attempt, using any already captured reviewer as a precondition.
Success opens one account-bound subscription and waits for its protected head
before discovery. A late profile from the failed attempt cannot open a stream.
This retry does not recreate an established stream or discard its replay cursor;
established connection retries remain owned by the adapter.

The recovery follow-up passes all 92 affected browser cases, including successful
initial-admission retry and a second timeout with late profile refusal. Typecheck,
lint and browser typecheck pass. Two additional desktop/mobile native cases inject
one initial account-read failure, use keyboard Refresh, observe one real socket
bound to the recovered account, then require a future actual Portal invitation
to arrive without manual discovery reload or Internal membership. These cases
are collected; exact-image runtime remains pending.

## Demo source parity

Demo captures future canonical invitation transitions and projects their actual
invitation audit append into an in-process journal. The private proof retains
Invitation/Organization/revision, recipient route, separate target surface/role,
issuer, optional accepting actor and canonical transition time. The original
audit identity, actor, entity, correlation and audit time remain attached to the
published proof. These private records contain no names, raw tokens or token
hashes; outbound events use the same neutral contract as Production.

The projection checks the active parent/account, actual matching proof and
current administrative authority or persisted Internal/Board/Portal grant.
It also supports retained Board Admin acceptance of a Member target. Session
authorization remains at the owning command's existing before/after fence,
matching Production's division between command policy and source projection.

Journal proofs, source audits, publication identities, event arrays and counters
participate in the existing account/Organization → Work transaction snapshot.
The final command fence commits or restores them with invitations, receipts and
grants. Replay holds the same account gate, so it cannot observe tentative
publication. Published proofs remain attached to their immutable audit sources
after later transitions. Duplicate publication of a revision is refused.

Raw fixture/legacy rows do not manufacture creation history. A future actual
acceptance can capture and publish its own proven transition. Demo replay uses
the same protected account/email/revision cursor and bounded window logic;
unowned or substituted recipient reads are refused. This remains process-local
Demo state and resets on API restart. It introduces no shared file store,
broker or Worker inside the API. Sample-catalog reset endpoints remain separate.

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

Remaining implementation includes complete parent/issuer authority invalidation
and current native acceptance/concurrency/latency evidence. Parent/issuer authority changes and
account/email changes require current protected discovery rather than assuming a
creation event confers continuing access. Native two-client, disconnect, cookie
withdrawal, Portal separation, keyboard/mobile and latency evidence remain
required. This source does not complete PRD-03 or PRD-60.

Local validation passed the complete source SQL fixture against a fresh
104-migration PostgreSQL/pgvector database, restricted role provisioning and the
schema isolation catalog check. The original restricted routing-isolation
fixture also passed, including its unaudited Invitation cleanup. This repairs
the cleanup failure reported by CI for commit `5f3e0f8`; current CI still needs
to verify the forward migration. The Linux migration runner passed clean/repeat,
forward upgrade with no invented history, concurrent runner serialization and
failed/unrecorded migration rollback. The full .NET Release solution build passed
with zero warnings/errors; affected release scripts pass Bash syntax checks.
These PostgreSQL source/persistence results alone do not certify real sessions, Demo source
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

The retained-Admin regression first failed against the 103-migration database
through the actual restricted `AcceptPendingAsync` service with
`invitation_storage_unavailable`. After the forward migration, that same service
preserved the existing Board Admin for a Member target, committed one canonical
accepted source and acknowledged the original retry without duplicate history.
The expanded source SQL fixture also rejects an insufficient Member grant for
an Admin target and an inactive Admin grant, rolling back the acceptance proof
and recipient counter in both cases. These checks supplement the replay contract;
they do not replace cookie/browser acceptance.

Exact-image CI for `a619c1e` reached the invitation creation retry fixture and
failed its unchanged-publication assertion after a direct administrative
revocation update. That update correctly captured a new unpublished recipient
transition proof, so the pre-revocation snapshot no longer represented current
source state. The fixture now invokes the actual reviewed-owner HTTP revocation,
requires its canonical audit/proof/event identity, revision, timestamp and next
recipient sequence, and then compares the complete post-revocation publication
snapshot across original creation-receipt replay and expiry/refusal. It also
requires exactly one additional audit and no replacement invitation, route or
receipt. Bash syntax and diff checks pass; current exact-image execution must
prove the repaired fixture.

Demo API-host checks cover cross-Organization Internal/Portal/Board publication
and paged replay, exact original event identities on repeated reads, no Portal
Internal grant, retained Board Admin acceptance, duplicate-source refusal,
late invalid-correlation rollback, insufficient/inactive grant rollback,
revocation withdrawal after publication, no legacy creation backfill and
unowned/substituted recipient refusal. Existing creation and acceptance rollback
fixtures additionally compare committed recipient history after actor refusal,
exception/cancellation and exact retry. These in-process checks do not prove
cookie-bound recipient streaming, browser reconnect or accessible announcements.

Nine additional Demo API-host cases use actual authenticated WebSocket/SignalR
frames and session cookies. They cover Internal/Portal/Board canonical creation
and HTTP acceptance, content-free event fields, no Portal Internal grant, logout
withdrawal, original missed revocation identity/time on reconnect, no replay of
acknowledged events, duplicate subscription refusal, cancellation/replacement,
untrusted origins, anonymous negotiation and malformed cursor recovery. A
synthetic reader boundary changes the real account revision at the final
post-session binding check and verifies that the pending source page is withheld.
These in-process transport checks passed alongside a zero-warning Release build;
they do not prove concurrent native account changes, Production WebSocket
acceptance, browser consumption or current exact-image CI acceptance.

## Native missed-event recovery after actual socket closure

The desktop/phone Portal scenarios now interrupt the actual server-connected
transport during the browser offline interval, then permit reconnection after
an independent issuer creates the genuine missed invitation. Server/client bytes
are forwarded unchanged; only transport availability is faulted. The original
three event IDs/types/sequences, exact content-free envelopes, protected nonmember
reads, no document reload, focus on Refresh invitations, logout withdrawal and WCAG assertions
remain. Both cases pass in the final four-case recipient invocation. The original
baseline failed all four recipient cases. See
[routing and interruption evidence](browser-recovery-ci.md#current-board-membership-authority-and-actual-recipient-interruption)
for current runtime scope and release limits.
