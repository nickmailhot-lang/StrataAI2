# Verified-email invitation discovery and acceptance

ONBOARD-FR-004/005/009: `GET /me/invitations` derives the normalized email from the current account. A global identity transaction locks the account, revalidates its actual HTTP session after waits, requires an active verified account and rechecks session admission after the read. It borrows that transaction's connection. The bounded page contains at most 50 invitations and a nonempty UUID `nextCursor`; `after` seeks by invitation ID. Expired, revoked or accepted invitations are excluded. Refresh starts from the first page; this is a live listing rather than a frozen multi-page snapshot. Invalid cursors fail with 400. Unverified accounts receive 403, revoked sessions 401, and storage failure a masked 503. No token or token hash is returned.

The pending result discloses only the invitation's own organization ID, organization label, intended surface/role and expiry. Migration 016 adds the email/UUID cursor index and a recipient-visible organization label to the existing routing projection. The label is captured when the invitation is published; existing rows receive their organization label at upgrade. It grants no membership, private Board visibility or Portal access by itself. Organization renames can make that invitation label historical; opening an accepted Organization retrieves current authorized state.

`POST /me/invitations/{id}/accept` lets a verified account accept an invitation bound to its email without retrieving a bearer token. Initial routing is only a hint. The existing Organization transaction locks the active parent and membership rows, revalidates the actor/session, rereads the intended email and invitation, and checks the issuer's current active administrative role. Only an Owner issuer can grant internal Owner membership. A downgrade of current ownership still requires the existing separate confirmation policy. Token acceptance remains single-use.

New acceptances store `accepted_by_user_id` in the invitation and routing projection in the same transaction as membership or PortalAccess and the acceptance audit. Repeating the same invitation ID can return the completed acknowledgment only to that same active verified account, before invitation expiry, with current parent/issuer/session admission. It does not create another membership or audit, or restore revoked membership. Legacy accepted rows have no fabricated accepting actor and cannot use this acknowledgment path. Internal invitations create Organization membership; Portal invitations create only PortalAccess. Private Board participation remains separately authorized.

Database wall time is checked after invitation row waits and again when consuming the invitation. A failed audit, stale session or expired invitation rolls the owning transaction back. The MUI page at `/app/invitations` provides loading, empty, paging, refresh and keyboard acceptance states. A 15-second deadline bounds transport and body parsing; uncertain acceptance preserves the same invitation ID for a safe explicit retry. The client validates acknowledgment ID, Organization, surface and role before displaying success or an access link. Unmount fences late completion. Revoked sessions clear the view and return to sign-in.

An internal Organization invitation that activates a new or inactive membership
also writes `ORGANIZATION_MEMBER_ADDED` in that same transaction. Its subject is
the persisted `OrganizationMembership` ID, its actor is the accepting user, and
its safe metadata is `{}`. It is separate from `INVITATION_ACCEPTED`. An already
active membership, Board invitation, Portal invitation or completed natural-ID
retry does not publish another member-added audit. Failure to find the actual
active membership or to append this audit refuses and rolls back acceptance.
Migration 097 now projects that audit into the existing Organization stream
using the actual membership revision, timestamp and private activation proof;
see [the member addition source](organization-metadata-events.md#member-addition-source).
The existing Worker, protected replay and strict browser consumer support this
subject. Current database/release execution and member administration live
integration remain pending; the audit alone does not establish those results.

The required exact-image fixture now fails this second audit insertion after
acceptance and its first audit have been written, compares complete invitation,
routing, membership, Portal and audit state, then checks concurrent acceptance,
restart retry and body-proof acceptance against the actual membership ID. These
new persistence assertions await CI execution. Demo's audit adapter remains a
no-op, so Demo API tests cannot prove persisted audit publication.

The fixture also accepts a new Admin invitation while the recipient is already
active and requires the complete member-added audit history to remain unchanged.
It then removes that membership through the version-bound HTTP command, retries
the completed invitation and requires access to stay removed. A new Member
invitation must reactivate the same persisted membership ID, append exactly one
new addition audit, and leave that history unchanged on another acceptance retry.
These are normal API commands against PostgreSQL, with SQL used for inspection;
the fixture does not manufacture membership transitions or event readiness.

For this audit increment, the Release solution build passed with zero warnings
and errors, all 42 selected invitation API regressions passed locally, and the
exact-image script passed Bash syntax validation. These source checks cover
recipient acceptance, Board/Portal separation and retry behavior; they do not
replace the pending real PostgreSQL second-publication rollback check.

An unconfirmed acceptance is held separately from the live pending page. If a committed invitation disappears on refresh, a generic explicit retry remains available with the original ID and expected acknowledgment fields. The retry panel displays no cached Organization label or grant details. Other acceptance controls wait until that attempt is resolved. A current authorization denial clears the attempt; a revoked/refused discovery session clears its details. The added component regressions and keyboard browser scenario confirm lost acknowledgment → empty refresh → same-ID retry, plus permission/session denial. The 143-test web suite, typecheck/lint and targeted browser check passed locally; release validation of this follow-up is pending.

Required exact-image CI covers one-connection reads/acceptance, audit failure rollback, concurrent duplicates, durable restart, bounded 101-row paging and revocation during an observed account-lock wait. API tests cover newly verified discovery, wrong account rejection, issuer revocation and Portal separation. Browser coverage loses a committed acceptance acknowledgment, retries the same ID using keyboard controls and verifies one authorized Organization entry. [Commit 8bd9586 CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36821144324) passed all nine jobs with three retained exact-SHA artifacts, including these release fixtures and the ten-test browser suite.

Invitation mail delivery, create/revoke idempotency, invitation domain-event publication and broader onboarding lifecycle/ownership/telemetry requirements remain outstanding. This increment does not close PRD-60 or the architecture tickets.

## Fresh discovery after refresh

Starting a discovery refresh or next-page read clears the previously displayed
invitation page and prior acceptance notice. A failed, stalled, malformed or denied
read cannot leave cached Organization/Board labels and ordinary acceptance controls
available as though they were current. Explicit Refresh starts from the first page,
as before; successful validated discovery repopulates the view. No read retries
acceptance or interprets a missing invitation as a successful acknowledgment.

An uncertain acceptance remains separate, displaying only its generic recovery
button. Its bound ID/Organization/surface/role/Board target are retained in memory
for an explicit same-invitation retry, including after a failed discovery refresh.
Current 401/403 discovery still clears that attempt. Two component cases verify
in-flight/failing refresh disclosure clearing and generic retry recovering the
matching Board acknowledgment. All 21 invitation discovery component cases pass
locally; lint passes. Exact-image release regression remains pending.

## Reviewed recipient account

The recipient listing and natural-ID acceptance endpoints accept an optional
`expectedActorId`. An empty or mismatched value receives neutral
`session_unavailable` before cursor parsing, protected discovery or acceptance.
The browser captures the current account, binds listing/paging and acceptance to
it, and confirms `/me` before and after each read or command. A changed account
withdraws labels, consent and acknowledgment links and returns to sign-in.
Temporary or malformed account confirmation also withdraws private display and
blocks acceptance. A submitted attempt remains as generic process-local recovery;
fresh discovery must confirm the original account before explicit same-ID retry.
Recovery never automatically posts or interprets an empty list as acceptance.

Component/API checks cover account changes on both sides of recipient operations
and Organization, Portal and Board target separation. Required release
fixtures compare complete persisted state for refused reviewed actors. New native
browser scenarios inject a cookie switch or account-read failure after a real
committed acceptance, check withheld disclosure, and verify original-account
recovery with exact natural-ID acknowledgment and unchanged history. Their runtime
results remain pending exact-image CI. This does not establish a recipient live
event stream or complete the remaining onboarding lifecycle requirements.

## Expiry during recipient review

Recipient discovery schedules a bounded timer for the nearest pending invitation
expiry. It withdraws the cached page, queues a fresh account-bound first-page read,
and fences obsolete reads. Expired rows returned by a racing read are hidden;
they cannot keep an acceptance button or cause an expiry refresh loop. Long
delays are chunked within browser timer limits. Ordinary acceptance checks expiry
again after current-account admission, before any POST. This path creates no
uncertain mutation or inferred grant.

An attempt already submitted retains its generic original-ID recovery even if
browser time reaches expiry. Explicit retry still checks the original account
and exact acknowledgment fields; the API retains its current issuer, scope,
expiry and membership policies. It may refuse recovery. A missing pending row
or a local expiry never confirms acceptance.

Component checks cover Organization, Portal and Board expiry, delayed admission,
and explicit submitted-attempt recovery. `recipient-invitation-expiry.spec.ts`
adds six desktop/mobile normal API scenarios with browser clock control, unchanged
history and no POST before expiry withdrawal, followed by a lost actual committed
acceptance response and exact-ID recovery with unchanged history. Only browser
time advances, so these cases do not prove server-clock expiry or override it.
Native execution remains pending exact-image CI.

## Complete recipient request deadline

Discovery/paging and each explicit acceptance/recovery now share one 15-second
deadline across both account checks, invitation HTTP I/O and successful JSON
reading. Per-request time allowances cannot accumulate. An aborted workflow
cannot start another request or publish late labels, consent or acknowledgment.
The loading state ends and explicit recovery becomes available at the deadline.
An acceptance already submitted retains only its original natural-ID recovery;
an unavailable final account check requires fresh original-account confirmation
before that retry. A discovery timeout never submits acceptance.

All 46 invitation-page component cases pass, including seven aggregate-delay
cases for discovery and Internal/Portal/Board command JSON/final profile JSON,
late completion refusal and exact original-ID recovery. Typecheck and lint pass.
Two additional native desktop/mobile Portal scenarios hold an account check and
the response of an actual committed acceptance across the aggregate deadline,
then check keyboard same-ID recovery, unchanged issuer history, separate Portal
admission and accessibility. They are collected/typechecked; their exact-image
runtime results remain pending. See also the separate
[recipient journal and protected transport](invitation-recipient-events.md).

The standalone `/app/invitations` route owns a semantic main landmark. It can
serve recipients without Internal Organization membership and therefore owns
its content structure outside the protected Internal application shell. Native
expiry checks keep all consent, original-ID recovery and mutation assertions
before resuming the browser clock for the full Axe accessibility scan.

All six recipient expiry/recovery scenarios pass against the real local
Development Demo API and Vite app across Internal, Portal and Board surfaces at
desktop/phone widths. Each verifies unchanged history before withdrawal, one
actual committed acceptance with a lost response, exactly one explicit recovery
using the same ID, unchanged history afterward, no document reload and the full
Axe scan. The fixture observes the actual committed acceptance on the protected
recipient stream and drains its browser invalidation before reviewing recovery;
otherwise that source can legitimately cancel a retry in flight. No server clock,
membership, audit, source or lease is modified to force the result. This local
Demo evidence does not replace exact-image release verification.
