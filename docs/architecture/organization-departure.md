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
fresh membership review before another explicit confirmation. An inaccessible
read reports unavailable access; it does not infer that a lost request succeeded.
Server/edge details are never displayed as trusted product text.

Reads and writes have a 15-second deadline, route changes abort pending work,
and late results are fenced by the mounted route/controller. Leaving does not
delete the Organization or its shared work. Departure still lacks a durable
same-key receipt; current-state reconciliation is not acknowledgment replay.

Six focused source cases pass: confirmed success and cancellation, sole-owner
refusal, lost-response review, and 401/403/404 withdrawal. TypeScript and lint
pass. The desktop/phone keyboard release scenario checks Cancel focus/no write,
sole-owner refusal, successful member departure, authoritative directory/access
withdrawal, success focus and WCAG 2.2 AA automated checks. Native execution is
pending CI; no full PRD closure follows from source checks.

The preceding full web recheck passed with exit 0: 1,499 tests in 122 files,
214.14 seconds. It predates this departure increment and does not verify the new
screen. The earlier two Board recovery timeouts passed in that recheck after
polling query overhead was reduced without relaxing assertions or timeout limits.
