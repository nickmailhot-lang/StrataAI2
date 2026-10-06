# Authorized Organization metadata replay

Return to the [documentation index](../README.md) or the
[metadata source and Worker delivery](organization-metadata-events.md).

Production exposes `GET /organizations/{organizationId}/metadata-events` with
optional `cursor`, `limit` (1–100, default 50) and `expectedActorId`. Responses
use `private, no-store`. Internal active Organization membership is required;
Portal access alone grants no admission. Demo does not map this endpoint because
its canonical metadata event source is not implemented yet.

The response contains an opaque cursor, `hasMore`, `pending`, `resetRequired`
and event envelopes. Each envelope carries the original source event ID, type,
actor ID, Organization ID, null Board ID, Organization entity ID/type, canonical
version, timestamp and exactly empty metadata. Names, descriptions, email and
logo URLs are absent. This channel currently contains only creation and metadata
editing events; it does not claim invitation/member or terminal-event replay.

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

Nine local replay-ordering/coordinator tests passed. The protected-cursor test
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
refusals. Exact-image execution for replay remains pending CI.
SignalR transport, browser reconnect/consumption, real mid-read session
withdrawal, remaining event types and Demo parity are still unfinished.
