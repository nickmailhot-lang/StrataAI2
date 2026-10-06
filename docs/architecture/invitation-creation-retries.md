# Retry-safe invitation creation

## Reviewed account binding

Organization and Board invitation creation accept optional `expectedActorId`.
The creation page captures the account used for its administrative admission and
sends that ID on both first submission and same-key recovery. A different or empty
reviewed actor receives neutral `401 session_unavailable` before role parsing,
receipt lookup, invitation publication or mutation. The draft and retry key remain
reserved for the original account; a replacement account cannot submit them under
its own authority. Existing API clients may omit the optional binding and still
use normal current-account authorization.

Local API checks cover both surfaces: mismatched/empty review leaves history empty,
then the original account successfully uses the same key and receives one stable
invitation on retry. Component checks verify the reviewed account query is retained.
The exact-image fixture additionally compares invitations/routes/receipts/audits
and private creation proofs, canonical sources, sequence and jobs across refusal.
These added runtime checks await CI. Release browser interceptors match URL paths
so account query parameters do not bypass committed-response loss injection.
The page now rechecks `/me` after administrative admission, before submission and
after its response. Board names and retained drafts are withheld until the final
admission check. Changed accounts clear private controls/acknowledgment and return
to sign-in; stored original-account requests remain reserved for later freshly
authorized recovery. A post-commit account change does not erase the request or
pretend the committed invitation was rolled back.

Component race cases exercise both Organization and Board surfaces at each of
these checks, including no POST after preflight account replacement and suppressed
acknowledgment after a committed response. The native release scenario creates the
invitation through the actual endpoint, signs in another account before returning
the response to the page, requires no visible acknowledgment or private email,
then signs in the original account and recovers the same key/body and canonical ID
with exactly one history row. Both viewport widths and surfaces are covered.
Execution of those real cookie/recovery cases against exact images remains pending;
mocked profile checks alone do not certify all mid-session behavior.

Temporary account-check failure (unavailable/malformed response, transport error
or bounded timeout) also withdraws display authority: Board name, email/grant
input, acknowledgment, preferences and captured actor are cleared. This is not
treated as confirmed sign-out or deletion of the original intent. The permission
check button performs fresh admission and restores only that account's reserved
request; it never automatically sends another POST. Explicit same-key retry then
recovers the original receipt. Component checks cover failures before submission
and after acknowledgment for both surfaces, plus malformed and transport cases.
The release fixture additionally returns a one-time 503 for `/me` after real
invitation commit, verifies private display withdrawal and one persisted history
row, then recovers via fresh permission check and identical key/body. Only the
account-read response is fault-injected; command, invitation and receipt remain
actual runtime state. Exact-image execution is still required.

Four focused timeout cases hold `/me` unresolved despite cancellation, before POST
or after its response on each surface. Advancing beyond the 15-second request
bound must abort the account read, withdraw private display and preserve the
original stored intent. A later successful profile result must not restore input,
publish acknowledgment or send another POST. Fresh explicit permission checking
can recover the reserved request and still cannot automatically mutate. These
tests exercise the client boundary with fake time and abort-ignoring transport;
they are not PostgreSQL, actual cookie-expiry or release runtime evidence.

`POST /organizations/{organizationId}/invitations` accepts an optional nonempty UUID `Idempotency-Key`. A keyed request acknowledges the original invitation ID, email, surface, role and expiry; its `invitationToken` is null in every runtime mode. Unkeyed Demo requests retain the existing bearer-token fixture behavior. Production never returns the bearer token.

The existing Organization unit of work locks the active parent and actor membership before invitation creation or replay. Current session/account eligibility and administrative role are authoritative; only a current Owner can acknowledge an internal Owner grant. Normalized email, surface and role bind the key to the original command. Different intent receives `409 idempotency_key_reused`; a receipt older than 24 hours receives `409 idempotency_key_expired`. Expired keys remain reserved, so an old request cannot create a replacement invitation. Malformed, multiple and empty keys fail with `400 invalid_idempotency_key`.

Migration 022 adds an explicitly tenant-scoped receipt containing a fingerprint and canonical invitation reference, with no bearer token, token hash or response JSON. A composite foreign key binds that reference to its Organization and original creating actor. Forced RLS applies to every receipt query and write. The restricted API role has only SELECT/INSERT on receipts; the Worker receives no receipt privileges. Migration 022 is required by the production schema guard. The prior supported schema upgrades without fabricating receipts for old invitations.

Invitation insertion, global routing projection, audit and receipt commit in the same owning transaction on the same borrowed connection. Parent serialization ensures concurrent identical requests see one completed invitation. Failed audit or receipt insertion rolls everything back, allowing the same key to execute after repair. A final production unit-of-work actor check rejects wall-clock session expiry during a write wait and rolls back that transaction. Replaying an acknowledged creation after invitation revocation/acceptance or member removal returns only its historical creation acknowledgment; it never restores an invitation, membership or Portal relationship.

The creation store returns its persisted canonical record. PostgreSQL uses `INSERT ... RETURNING` before acknowledgment; the first response therefore uses the same stored timestamp precision as replay. The required fixture retains strict byte-for-byte response equality. CI for a5b3b9d found differing concurrent acknowledgments; that commit has no green release claim. The follow-up canonical-record fix adds a deterministic host case using a provider that persists microsecond timestamps, plus safe public-ID/expiry diagnostics on fixture mismatch. Execution of the fix remains pending in its own CI.

Demo keeps its receipt dictionary under the shared process-local command gate. This demonstrates retry semantics but does not claim crash durability or transactional rollback. Existing general Organization retry receipts and invitation revoke retries remain outstanding. Persistent expired keys currently remain as tombstones linked to canonical invitation records; complete privacy/deletion retention policy and processing remain separate requirements.

Validation includes API-host cases for stable token-free acknowledgment, normalized intent, changed-key rejection, revoked-invitation acknowledgment, invalid keys and current grant authority. The local browser-context case uses real verified accounts, proves one discoverable invitation, accepts it, removes access, then retries creation without restoring access or rediscovering the consumed invitation. Exact-image CI is mandatory for restricted PostgreSQL concurrent retry, audit/receipt failure rollback, post-write session expiry rollback, expired-key reservation, post-wait demotion, forced RLS and cross-tenant reference rejection. Local build/Bash/YAML checks and Demo execution do not prove those PostgreSQL results.

This change does not claim email delivery. The [administrator invitation form](invitation-administration-ui.md) now consumes the token-free receipt. Worker delivery, invitation-driven registration with self-registration disabled, invitation history/events, revoke receipts and full PRD-03/60 acceptance still require implementation and release verification.
