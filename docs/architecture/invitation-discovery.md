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

## Explicit database wait gates (2026-10-15)

The required invitation fixture now holds its expiry and account row locks in
interactive PostgreSQL transactions. It observes the holder's backend after its
`SELECT ... FOR UPDATE` completes, then requires the actual API backend to be
blocked by that specific holder through `pg_blocking_pids`. After that observation,
it expires the invitation in the holding transaction, or commits session
revocation through a separate connection, before explicitly releasing the lock.
An unrelated API lock cannot satisfy the observation. A 60-second idle transaction
limit bounds an abandoned holder; exit cleanup closes its input and removes the
owned scratch files.

This replaces two fixed ten-second sleeps that competed with the fixture's
unchanged ten-second API database command timeout. Hosted run
[38677909216](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38677909216),
job 116258431823, failed the final `401` assertion. Its retained database log
shows cancellation while locking the account row, but does not establish the
HTTP status or a product authorization defect. The complete unchanged local
baseline returned the required `401` and passed; there is no unchanged local
reproduction claim.

The revised fixture retains the single-connection API, request limits, exact
expiry `400` and session `401`, privacy exclusions, canonical rollback snapshots,
concurrent/restart acceptance recovery, membership activation history and the
bounded 101-invitation pagination case. It is functional verification; large
load testing remains disabled until after the operational MVP.

The complete revised script passed locally in 101.764 seconds against the
archive-verified `24f5624` API image and fresh PostgreSQL with all 149 migrations
and restricted runtime roles. All 388 captured inputs matched after execution;
owned cleanup restored the original Docker inventory and removed 2,577,091
temporary bytes. No original database, application rebuild, image import or
download was used. The frozen Windows runner replaces only the two Linux UUID
file reads with a standard UUID generator and prints the final HTTP status
before its unchanged assertion. This retained-image check does not certify
current-main images or a complete release. All 241 CI coverage guards, Bash
syntax and workflow lint also pass; full current-candidate CI remains required.

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

## Recipient source-calendar admission (2026-10-16)

Recipient discovery and proof review previously accepted finite parsed expiry
instants that JavaScript silently normalized from impossible source calendars.
Discovery also admitted offset-free input, interpreting it in the browser's
local timezone. Both consumers now reuse the existing strict source-calendar
validator before publishing invitation metadata or acceptance controls. Link
review retains its additional future-expiry check.

The change does not consume a proof, accept an invitation, rewrite source
clocks or alter account/epoch/deadline/recovery rules. Invalid discovery uses
the existing generic Refresh error. Invalid proof review keeps the existing
generic review error and permits fresh explicit review with the same proof;
acceptance requires a valid reviewed response. Null/offset-free expiry is not
a valid invitation instant. Real leap days, explicit offsets and seven
fractional digits remain valid.

All **seven new malformed-expiry regressions fail** against the original
source: Internal, Portal and Board discovery/review calendars, plus offset-free
discovery. Six valid leap-day/offset counterparts pass. After repair all **127**
focused recipient discovery, live discovery, proof review and date-display
cases pass. Tests require hidden metadata/consent, zero acceptance requests and
fresh valid recovery; proof retries retain their exact original body.

The new desktop/phone native scenarios use an actual separate Worker-delivered
Internal invitation proof. They keep malformed intercepted expiry active during
discovery privacy checks, restore actual data before Refresh, then separately
withhold proof-review consent on a malformed response. Explicit re-review uses
the same proof and current actor. Canonical pending state/expiry, zero acceptance
requests, continued denial of Organization/private Board access, URL/storage
proof exclusion and WCAG checks remain required. These Internal native checks
do not stand in for Portal/Board native acceptance; those surfaces have scoped
component coverage in this increment and retain their full release requirements.

PRD-02 stays open at **16%**, PRD-03 at **8%**, PRD-05 at **14%** and PRD-60 at
**18% estimated work remaining**, unchanged planning estimates. Current
immutable/full CI and all original acceptance remain required. Large-load
testing stays deferred until after the operational MVP.

The initial native fixture stops both cases before browser validation because
invitation-mail publication is disabled by default. Its **0/2** report is
preserved, with captured inputs unchanged and original Docker inventory
restored; **115,064 temporary bytes** are removed. The new proof-dependent
cases are now owned by an explicit required `browser-notifications` mail-enabled
phase, using the existing isolated provider and retained images. Their dedicated
configuration inherits normal sequential/no-retry policy, and a runtime report
verifier requires both actual desktop/phone passes without skipped/retried cases.
The default general browser configuration does not select this `.case.ts` file;
no existing scenario is removed or skipped. Entry/cleanup preserve the previous
mail and verification settings, and neither entry nor restore may pull images.

The first mail-enabled local setup correctly refuses an unscoped invitation
Worker before any browser case (17.8 seconds, 3,359 temporary bytes removed and
Docker inventory restored). The fixture now keeps global identity delivery
mail-enabled but invitation delivery disabled until each case owns its actual
Organization through the original scoped-Worker helper. Case cleanup restores
the unscoped Worker with invitation delivery disabled. The startup refusal,
explicit actual-tenant scope and original helper remain intact; no fabricated
Organization ID or broader Worker scope is introduced.

The final complete dedicated desktop/phone file passes **2/2 in 90,225.506 ms**,
with no failures, flaky/skipped cases or report errors. Its runtime report also
passes the new independent two-viewport/no-skip/no-retry verifier. Both accounts
consume real Worker-delivered verification proofs before login under strict API
and Worker verification. An actual scoped invitation-mail effect supplies the
review proof; no SQL fabricates a proof, membership or timestamp. UI recovery
retains the same actual proof/body and actor, sends zero acceptance POSTs,
preserves the issued expiry and pending/unrevoked record, and keeps Organization
and private Board reads at 404. URL/storage proof exclusion and WCAG checks pass.

All **1,284 captured source, 71 binary, four served asset** and private Compose
hashes match. API identity/start remain stable; the original scoped helper
restores the same cached Worker healthy and unscoped with invitation delivery
disabled. Original Docker inventory is restored and **3,721 temporary bytes**
removed after success. No images are built, pulled or imported. The fixture uses
current frontend/read-only compiled hosts (local runtime `13d9d83c`), fresh
restricted PostgreSQL17.11/schema149 and a controlled provider. It does not
certify external inbox delivery, managed providers or retained current images.

All **127** focused web checks, web/browser types, lint, Bash syntax, actionlint,
**289 CI coverage guards plus ten collection/report checks**, 1,954 local doc
links and 611 anchors pass. The new required phase is registered in the original
integration owner inventory; source checks reject bypass, ignored failures,
missing pacing or omitted source verification. Its wrapper preserves prior
settings and validates a fresh runtime report; local case execution and syntax/
source guards do not replace hosted execution of that wrapper or full exact-
commit build-once CI. No existing case, policy or acceptance criterion is dropped.

## Exact mail phase entry and restoration (2026-10-16)

The actual [CI wrapper](../../scripts/ci/test-recipient-clock-browser.sh),
[dedicated configuration](../../playwright.recipient-clock.config.ts), browser
cases and runtime-report verifier are executed together in a fresh isolated
local Compose topology. Their staged bytes are unchanged from `c02b6547`. This
extends the preceding case-only evidence; it is not a hosted current-image run.

The complete wrapper exits **0** and both native cases pass **2/2 in
89,970.608 ms**, with zero failures, flaky/skipped cases or report errors. The
wrapper's own fresh-report gate accepts both actual viewport results. Entry
forces strict account verification, enables API invitation publication, and
keeps the global Worker invitation handler disabled until the original helper
selects each real Organization. Cleanup recreates the API/Worker from the same
cached images, restores API invitation publication **off** and verification
**on**, and restores the Worker healthy/unscoped. API/Worker recreation is an
intentional fixture boundary here; this is not process-continuity evidence.

All **1,282 captured repository inputs, 71 binaries, four served web assets**
and staged script/configuration/helper bytes match. Original Docker inventory
is restored and **41,766 temporary bytes** removed, including the bounded
private staging copies; the verified dependency junction is unlinked without
recursively touching its source. No images are built, pulled or imported.
The topology uses current frontend/read-only compiled hosts in cached runtimes,
fresh restricted PostgreSQL17.11/schema149 and the owned controlled provider.
Runtime remains `13d9d83c`; managed delivery, durable public deployment and full
current exact-commit immutable CI remain required.

Two earlier local setups stop before browser execution: one incorrectly gives
the private API its Worker's port; the next lacks the copied standard reporter
module. Both are fixture-only setup failures, retained separately, and restore
Docker inventory after removing **6,306** and **29,945 temporary bytes**,
respectively. Correcting those staging prerequisites does not alter the wrapper,
report gate, product configuration rules, browser assertions or deadlines.

PRD-02 stays open at **16%**, PRD-03 at **8%** and PRD-60 at **18% estimated
work remaining**, unchanged planning estimates. Large-load tests stay deferred.

A separate negative fixture starts with API invitation publication **off** and
account verification **off**. It changes only the private staged case copy to
mark both scenarios skipped; repository tests, wrapper, configuration and report
verifier remain unchanged. Playwright records zero passes/two skips, and the
original report gate refuses that result. The exact wrapper exits **1**, then
its original EXIT cleanup restores invitation publication **off**, verification
**off**, and the Worker healthy/unscoped. Thus a successful CLI exit with no
actual acceptance execution cannot certify the phase, and failure does not
leave the temporary stricter API setting behind. This is an intentional rejected
report, not a passing product/browser run.

All **1,284 captured repository inputs, 71 binaries and four served assets**
match; exactly the one declared private case copy differs. Original Docker
inventory is restored and **44,098 temporary bytes** removed. No images are
built, pulled or imported. Together with the positive run, this verifies the
original wrapper's success and report-refusal cleanup paths under the tested
prior settings. It does not replace hosted execution against the current
retained images or broaden the PRD acceptance claim.
