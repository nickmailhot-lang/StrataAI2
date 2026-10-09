# Reviewed Organization departure

PRD-03 WS-FR-006 has a MUI departure screen at `/app/{organizationId}/leave`,
linked from the active internal Organization directory for every admitted role.
It uses a direct current Organization read rather than an unbounded directory.
The internal surface guard and server membership/session checks remain
authoritative; Portal-only access does not authorize departure.

The confirmation names the Organization, explains loss of membership and Card
assignments, and warns that another usable owner must remain. Cancel receives
initial focus. Cancel and Escape send no mutation. Confirm submits one POST to
the existing departure endpoint; duplicate in-flight requests are refused.
Only HTTP 204 reports departure success. The success announcement receives
return focus and provides a link to the user's Organizations.

The server serializes departures under the Organization parent and membership
locks, protects the usable-owner floor, and atomically removes assignments and
records the departure audit. A sole-owner refusal explains the continuity rule.
The UI clears private review data after refusal or uncertainty and requires a
fresh membership review after a definitive refusal before another explicit confirmation.
An uncertain response retains the original command key and blocks fresh review
until that command is acknowledged or definitively refused. An inaccessible
read reports unavailable access; it does not infer that a lost request succeeded.
Server/edge details are never displayed as trusted product text.

Reads and writes have a 15-second deadline, route changes abort pending work,
and late results are fenced by the mounted route/controller. Leaving does not
delete the Organization or its shared work. Departure uses a durable same-key receipt for acknowledgment recovery;
current-state reconciliation is not acknowledgment replay.

Ten focused source cases pass, including same-key recovery after later rejoin,
repeated uncertainty, receipt expiry and access withdrawal. Earlier cases cover: confirmed success and cancellation, sole-owner
refusal, lost-response review, and 401/403/404 withdrawal. TypeScript and lint
pass. The desktop/phone keyboard release scenario checks Cancel focus/no write,
sole-owner refusal, successful member departure, authoritative directory/access
withdrawal, success focus and WCAG 2.2 AA automated checks. Native execution is
pending CI; no full PRD closure follows from source checks.

The preceding full web recheck passed with exit 0: 1,499 tests in 122 files,
214.14 seconds. It predates this departure increment and does not verify the new
screen. The earlier two Board recovery timeouts passed in that recheck after
polling query overhead was reduced without relaxing assertions or timeout limits.

## Durable departure API acknowledgments

POST departure now accepts an optional nonempty UUID `Idempotency-Key`. Its
token-free receipt commits with membership retirement, assignment cleanup and
audit in the owning Organization transaction. Failure to publish the receipt
rolls back the command. The Organization parent/membership locks serialize
same-key requests; matching replay acknowledges the original command without
retiring a later rejoined membership or removing its assignments.

Replay requires the same current account/session and an active Organization.
It does not require the old membership to remain active, because departure
intentionally retires it. The reply contains no Organization/member details and
does not grant access. Another account cannot use that receipt. After 24 hours,
the key returns `idempotency_expired` and remains reserved. Receipt retention
and cleanup still need a defined operational policy.

Migration 083 forces tenant RLS. The API receives SELECT/INSERT only; the Worker
has no receipt access. Both runtime hosts require its ledger entry. Demo receipts
participate in owning Organization rollback. The browser retains the original UUID and reviewed account through uncertain
responses. Retry sends the same body and key. Its acknowledgment describes the
original departure and requires a new current-membership read before offering
another departure; it does not retire a later rejoin or claim current access is gone.
The optional expectedActorId request field binds browser confirmation to the
reviewed account. A different current principal receives session_unavailable
before membership mutation or receipt lookup/publication. Legacy callers may
omit the field; server authorization always uses the current principal.

API-host coverage checks concurrent same-key departure, replay after rejoining,
unchanged rejoined membership/receipt and revoked-session refusal. The mandatory
native fixture denies receipt INSERT and compares membership/audit/receipt state,
observes two requests waiting on the parent lock, requires one audit and receipt,
and checks later rejoin, actor isolation, expiry, tenant reads and restricted
privileges. Strict compilation and Bash syntax pass; native execution and
post-publication expiry/assignment rollback evidence remain pending.

The full web run for departure browser revision `078df3b` completed with 1,504
passing cases and one 10-second timeout in the existing Card comment-recovery
case. That case passed in isolation; its repeated whole-page polling queries
are being reduced while keeping the same attached/enabled checks and timeout.
This failed full run does not prove current release acceptance.

The API-host account-switch case checks refusal, unchanged membership and absent
receipts for both actors, then successful departure with the correctly bound
account and the same unused key. The desktop/phone browser scenario loses an
actual committed response, rejoins, replays the original acknowledgment and
checks preserved current membership, identical request body/key and keyboard
recovery. Focused browser source tests pass and the API project compiles without
warnings; native API/database/browser execution remains pending CI.

Demo final-actor-loss coverage uses a keyed departure through the real
Organization service. Its receipt-store fixture first completes the actual
in-memory publication, then withdraws actor admission before the final
transaction check; this observes the precise post-publication rollback boundary. It checks that rollback removes the published receipt
and restores membership, Card revision, assignment and Work events. Retrying
the same key after restored session admission commits the departure and receipt.
The API-host project builds without warnings; execution still requires CI.

The mandatory PostgreSQL fixture now observes the API runtime waiting in an
AFTER INSERT departure-receipt trigger while the actual cookie session expires.
It requires session_unavailable/401, no cookie or private identity disclosure,
unchanged membership/audit/receipt state and unchanged user/session rows. It
restores the original expiry and retries the same key through the existing
concurrent acknowledgment checks. Cleanup removes the trigger and restores
the session on failure. Bash syntax validation passes; actual container
execution is pending, and native assignment rollback coverage remains needed.

The same native fixture also submits a departure reviewed for the owner with
the other member's cookie. It requires session_unavailable/401, no actor or
Organization identifier disclosure and unchanged membership/audit/receipt state
before any subsequent valid command. This complements the API-host account
switch case; actual CI execution remains pending.

The mandatory container fixture now creates and assigns a real Card before
departure/removal receipt tests. Receipt INSERT denial and observed session
expiry after receipt insertion must preserve membership, assignment, full Card
record, immutable Work event fields, stream counters, audit and receipt state.
Worker delivery markers are excluded from the event comparison because delivery
can advance independently. Removal checks also compare account/session rows and
refuse cookie/private disclosure. Successful concurrent commands remove the
assignment and advance the Card version once. After rejoin, an actual API
assignment is restored; original receipt replay must preserve it unchanged.
Cleanup drops the temporary trigger and restores session expiry on failure.
Bash syntax passes; actual container execution remains pending CI.

## Reviewed account and bounded acknowledgment

The departure screen checks the complete active account profile before and after
its protected membership read. It exposes the Organization name and departure
consent only when both profiles match. A confirmed account replacement withdraws
the review and navigates to sign-in, even when both accounts could otherwise see
the Organization.

Departure and original-key recovery check the reviewed actor before sending the
existing `expectedActorId` command body and again after reading the response. A
204 becomes an acknowledgment only after that final check. Confirmed account or
access withdrawal clears the original recovery key and any notice. Temporary
account uncertainty before a fresh submission sends zero departure requests,
withdraws the private review and explicitly says no departure was sent. It does
not invent a pending command. Uncertainty after an actual submission preserves
the same actor, key and immutable body for explicit acknowledgment recovery;
normal receipt policy still decides whether a later rejoin remains intact.

Each read or departure attempt has one 15-second bound across all profiles,
protected requests and response bodies. Abort and route ownership checks fence
late replies. Local component checks cover account replacement across discovery,
before/after departure, transient profile failure, same-key recovery,
noncooperative fetches and a shared deadline consumed by an earlier slow profile
plus a later stalled JSON body. Current native coverage adds four desktop/mobile
real-cookie replacement cases and extends the two existing departure scenarios
with zero-command/unchanged-membership refusal and actual 204 withheld during
profile uncertainty, followed by rejoin and same-key recovery. These browser
cases require exact-image execution; source collection is not runtime proof.

## Executed desktop and phone departure recovery

All six existing native scenarios pass in one 3.2-minute invocation at 1280px
and 390px through current Nginx/CSP, with the current compiled Production API,
production web bundle and real restricted PostgreSQL schema-110 roles. The API
uses the same explicitly unverified-email test policy as CI's browser phase;
these cases do not establish the default verified-email ownership matrix. The
source tests and their assertions were unchanged. Production rate limiters remain
enabled and the existing release pacing fixture runs between scenarios.

Four cases replace the actual browser session cookie before or after departure.
Before submission, no command is sent and the original membership is unchanged.
After committed submission, the membership is removed, but the changed account
receives no stale success or private retry state. Both paths withdraw the review
and navigate to sign-in without a full document reload; WCAG checks pass.

Two complete continuity scenarios verify sole-owner refusal, Cancel focus/no
mutation, member departure with authoritative directory/access withdrawal and
success focus. A successful response is deliberately lost, the member rejoins,
and retry acknowledges the original key/body without removing the restored
membership. Pre-submission profile uncertainty sends no command and preserves
membership. Post-commit uncertainty withholds success, then original-key recovery
after a later rejoin preserves that later membership and returns accessible focus.

This closes a local execution gap, not retained-current-image or complete PRD
acceptance. Current release CI, strict-policy ownership, server transaction and
remaining lifecycle requirements still need their own evidence. Estimated PRD-03
work remaining is **9%**, a planning estimate; the ticket stays open.

## Departure navigation after real Home admission

The retained phone trace stays on Organization Home after its link keypress;
the subsequent departure control never appears. The fixture now observes the real
actor/Organization metadata head and a successful Home read started after it,
then admits enabled focused controls before one keypress. Both widths pass in the
final six-case native invocation. Sole-owner refusal, cancellation/no-write,
actual membership removal, lost-response original-key recovery after rejoin,
pre/post-command profile uncertainty, preserved later membership and focus/WCAG
assertions remain. See the
[complete execution scope](browser-recovery-ci.md#automatic-organization-metadata-routing-in-native-browser-acceptance).
Estimated PRD-03 work remaining stays **8%** (planning estimate); full release and
lifecycle acceptance remain required before closure.

## Strict verified-email native departure

Both departure browser files now pass all six desktop/phone cases together under
strict verified-email admission against current compiled Production API, rebuilt
MUI assets behind Nginx/CSP and restricted PostgreSQL 17/pgvector schema 114. The
old immediate-login fixture fails on pending-account 403; the shared strict
fixture verifies only freshly registered disposable accounts before real login.
Sole-owner protection, explicit confirmation, Cancel/success focus, account
replacement/uncertainty, lost-response original-key recovery after rejoin, exact
later-membership preservation and accessibility remain. Original deadlines and
rate/retry policy are unchanged.

The new strict CI phase selects both complete files after identity verification
and checks API/Worker policy; the complete unfiltered browser phase retains the
optional-policy path. This synchronous command/receipt proof does not require
Worker delivery or certify provider mail/current immutable/full release gates.
See [executed scope and remaining acceptance](prd-03-acceptance.md#strict-verified-account-departure-and-continuity).
Owned test containers/database are removed and original services/data preserved.
