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
