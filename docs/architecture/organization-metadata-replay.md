# Authorized Organization metadata replay

Return to the [documentation index](../README.md) or the
[metadata source and Worker delivery](organization-metadata-events.md).

Both runtime modes expose `GET /organizations/{organizationId}/metadata-events` with
optional `cursor`, `limit` (1–100, default 50) and `expectedActorId`. Responses
use `private, no-store`. Internal active Organization membership is required;
Portal access alone grants no admission. Production uses its persisted source
and separate Worker; Demo uses the process-local journal described below.

The response contains an opaque cursor, `hasMore`, `pending`, `resetRequired`
and event envelopes. Each envelope carries the original source event ID, type,
actor ID, Organization ID, null Board ID, subject entity ID/type, canonical
subject version, timestamp and exactly empty metadata. Names, descriptions,
email and logo URLs are absent. Creation and editing use the Organization
subject. Migration 097 adds member additions with the actual
`OrganizationMembership` subject, whose version is independent of its parent.
The stream also includes canonical removal/departure and Internal Organization
invitation creation, acceptance and revocation sources. Terminal deletion remains
a separate lifecycle contract. Historical evidence below records earlier slices;
current exact-image acceptance remains required.

## Authority and recovery

The endpoint calls the owning transactional coordinator. It locks the active
parent and membership in the existing Work read transaction, verifies the
authenticated actor/session before and after the operation, and rechecks scope
before returning even empty/reset pages. The PostgreSQL adapter refuses reads
outside that owning tenant transaction. A failed commit or final authorization
check returns a masked failure with no page or cursor.

Data-protected cursors have a separate metadata purpose and bind Organization,
actor, membership ID and membership version, with a 15-minute lifetime. An
Organization rename does not invalidate the cursor: the new version is an event
to replay. Membership withdrawal/restoration or replacement invalidates the
previous authority. Another account, Organization, purpose, expired token or
malformed cursor cannot replay using that token.

No cursor, or an invalid/stale cursor, returns a reset instruction and an empty
page at the current head. The consumer must fetch an authoritative metadata
snapshot **after receiving the reset** before resuming normal consumption.
Bootstrap does not replay guessed historical versions. Accepted cursors replay
only a contiguous ready prefix; later ready events never bypass an earlier
unready event. Missing, duplicate or reordered history requires a snapshot
reset rather than silently advancing over the gap. Internal numeric positions
are not exposed. Consumers must deduplicate by source `eventId`.

Once the parent is DELETING/DELETED, normal metadata replay is unavailable,
including for its Owner. Original Owner deletion observation remains a separate
request-bound endpoint. Access withdrawal is not a fabricated terminal event.

## Evidence boundary

Ten local replay-ordering/coordinator and cursor-admission tests passed. The protected-cursor test
passed for account/Organization/membership/revision binding, wrong purpose,
malformed/oversized tokens, maximum internal position and expiry. The restricted
PostgreSQL fixture exercises the real owning read transaction, out-of-order
Worker readiness, bounded pages, retained source IDs, final synthetic session
refusal and removed/restored membership. These checks passed restricted
PostgreSQL CI at `fe3376f` ([job evidence](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37520463190/job/112464244715)). Its actor admission is synthetic and
does not establish HTTP session expiry behavior.

The exact-image fixture now bootstraps through the normal endpoint, observes
pending events before Worker delivery, resumes its original cursor after a
metadata edit, checks content-free source envelopes and wrong-actor/deleting
refusals. These checks passed the mandatory exact-image metadata step at
`fe3376f` ([container job evidence](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37520463190/job/112469522304)).
That overall job failed later because the Work-event crash-recovery fixture
reset/count included the new metadata job. The fixture now limits recovery to
Work jobs and checks that unrelated jobs and metadata readiness stay unchanged;
its repaired execution is pending. The metadata step alone does not establish
complete release acceptance.
Both runtime modes map the authenticated SignalR endpoint
`/organizations/live/metadata`, with stream method `Watch(organizationId, cursor)`.
It shares the existing same-origin guard and transport buffer limits. One
subscription is allowed per connection; cancellation releases that slot.
Every replay iteration checks the cookie session, reads through the owning
transaction, then checks the session and current cursor binding again before
delivery. Permission changes during session I/O discard the page. Cancellation
and connection loss stop the loop. Idle connections receive periodic heartbeat
pages; pending sources do not cause a busy loop. Stream execution against release
images is still unverified. Two API-host origin-denial checks passed locally.

The Organization discovery page now subscribes separately to metadata and Board
streams. A canonical metadata event or reset triggers fresh authorized metadata
and Board-directory reads with the existing before/after account checks. Cached
names and creation consent are withdrawn during recovery. The client checks
the runtime descriptor first and opens this channel in either mode. It validates
the complete content-free envelope, deduplicates immutable source IDs, rejects
changed source attribution, fences obsolete callbacks and resumes the exact
opaque cursor across reconnect. Empty heartbeat re-encryption is not a change.
All 67 focused client/discovery/runtime-mode checks passed locally. The prior full web
baseline passed 1,567 checks with two workers and unchanged assertions/timeouts;
that baseline predates the new browser consumer. Current full CI remains required.

The release-image scenario also requires the actual discovery heading to reflect
the canonical rename; its execution remains pending. Settings now use the same
channel with scoped account-bound reads, preserved typing and explicit saved-state
review, including original-save recovery after a later revision. All 24 focused
settings checks passed; release-image two-tab execution remains pending. See
[settings behavior and evidence](organization-settings.md#live-changes-and-preserved-drafts).
Other applicable views, real mid-read session withdrawal, Demo terminal/lifecycle
parity and complete current release acceptance remain unfinished.

## Demo metadata replay

The Demo journal projects future ordinary Organization commands into all seven
supported source types: creation, editing, member addition, removal/departure,
and Internal invitation creation, acceptance and revocation. Each source retains
its original audit ID and actor, plus the actual subject ID, revision and transition
timestamp. Same-command private proofs prevent guessed history. Board and Portal
invitation sources stay on their own surfaces. Metadata is empty and Board ID is null.

Publication simulates delivery synchronously inside the original command. The
owning shared Work gate prevents a reader from observing tentative writes. Source
history, publication identities and stream counters roll back together after a
late refusal, exception or cancellation; a waiting reader sees only restored
committed state. Original-key retries do not republish old acknowledgments.

Protected HTTP and SignalR replay use the same bounded pages, snapshot reset,
current session/membership checks and membership-bound cursors as Production.
The browser preserves drafts and original-save recovery in both modes. Demo
does not replace the separate durable Production Worker or connect to production
providers. API restart resets this in-memory journal; sample-catalog reset does
not reset it, accounts or authenticated Organizations.

On 2026-10-07, seven new API cases, all 91 selected Demo API cases, and four real
desktop/phone Demo browser scenarios passed. Browser cases retained their source
identity, reconnect, logout withdrawal, saved-version review, original-key/body,
keyboard and accessibility assertions. The local runtime used compiled API and
Vite output; the mandatory retained-image Demo CI phase still requires its result.
