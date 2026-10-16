# Board invitation history head admission

This supports the [operational MVP milestone](operational-mvp-milestone.md),
[invitation administration](../board-invitations.md) and the maintained
[live history scenario](../../tests/browser/board-invitation-history-live.spec.ts).

The retained browser artifact from [CI run 38310357937](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38310357937)
identifies commit `32cda04fae03ba009fe46381afa9109c9d3afbfe`. Its desktop history
case loses the Cancel control after a preference update. Four consecutive
protected history responses around that review contain the same two records
with no changed fields. This evidence does not prove that preferences caused
the closure or that the hosted failure has been reproduced locally.

A component regression uses the actual Board stream controller and a synthetic
connection to deliver a valid changed head. After the protected read finishes,
it opens a fresh revocation review and advances through the delayed refresh and
dialog transition. The original component loses Cancel focus. The repaired
component passes the same regression. An initial version observed focus before
the dialog transition completed and failed even with the repair; that version
is excluded from the before/after claim. The final regression allows the normal
transition in both runs and still distinguishes the original behavior.

History administration now opts into the existing immediate-invalidation path,
also used by invitation creation. A changed head withdraws stale authority
before reporting live instead of scheduling a later duplicate invalidation.
Protected Board/account reads still establish administration. Changed history,
expiry and lost access still retire consent; quiet unchanged preference updates
retain the chosen confirmation focus. No revocation is sent by the regression.

All **68 invitation-history and Board-stream component checks** pass. Web and
browser types, focused lint, fresh solution and web builds, and **213 integration
coverage guards** pass. After terminal build success, the complete original
desktop/phone history scenario passes **2/2** in **102,621.819 ms**, with zero
unexpected, skipped or flaky cases. Consent, Cancel focus, preference captions,
canonical records, no observer writes, live acceptance/revocation, reconnect,
accessibility and the original deadlines remain unchanged.

The browser capture uses PostgreSQL with canonical restricted runtime roles,
the actual compiled API and separate Worker, and Nginx serving the MUI build.
API registration verification matches this original full-browser scenario;
Worker verification and discovery remain enabled. Independent checks find zero
differences across **2,098 hosted source/runtime files**, **1,414 native source
files** and all **four web assets** before further edits. Owned containers,
database, roles and credential files are removed; original PostgreSQL is
preserved. Private traces and reports remain outside the repository.

Mounted compiled-host evidence does not certify retained release images,
managed mail or a complete release. Exact-current CI and the remaining original
acceptance criteria remain required. PRD-05 and PRD-60 stay open, with estimated
work remaining **14%** and **18%** respectively, unchanged planning estimates.

## Account-outage fixture window (2026-10-15)

[Run 38680871875](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38680871875),
job `116279084573`, passes 94 cases and fails both phone account-uncertainty
cases. Artifact `11959790247` is independently checked against its 3,518,410-byte
size, SHA-256 `ebbd963042446e0c68e5e69f5dab1c4eeec7db44c538f2fad3079d14ce4a9bd2`
and embedded candidate metadata. The failing file is unchanged from candidate
`b45e83d5609c576077e6a7d5bfe0cd7ef05c0a87` to the repair's parent.

The Organization trace contains a failed account read followed by a successful
account read and an actual revocation. The Board trace contains a failed
account read, no revocation, and a history-read error. These do not establish
a product authorization bug: the fixture automatically restored access after
one failed request, which could be a competing background read. The complete
unchanged local file passes **4/4 in 118,166.311 ms**; an unchanged reproduction
is not claimed.

The browser fixture now keeps the account unavailable throughout each intended
outage and explicitly restores it before Refresh invitations or Check revocation.
This matches the existing Organization/Board component scenarios. Original
pre-submission refusal and post-submission uncertainty messages, private-data
withdrawal, zero then exactly one DELETE, canonical unchanged/committed records,
one document navigation, accessibility, viewport checks and 90-second deadlines
remain intact. Product behavior and mutation activation are unchanged.

The complete revised desktop/phone file passes **4/4 in 118,759.569 ms**, one
attempt per case, zero skipped/flaky/unexpected outcomes or report errors.
All **51 invitation-history component checks**, browser types and **244 CI
coverage guards** pass. The browser capture uses archive-verified retained
`24f5624` images, fresh PostgreSQL/pgvector with 149 migrations and restricted
roles, plus current read-only Nginx configuration. API verification is optional
and mail disabled; the Worker retains its verified-account policy. All 172
browser input hashes and three web configuration hashes match. Original images
and database are preserved, full Docker inventory restored and **1,708,388
temporary bytes** removed. No images are built, downloaded or imported. Both
small diagnostic ZIPs for this follow-up and the label-reference investigation
are removed after compact evidence retention, reclaiming **8,567,099 bytes**.

These retained-image functional checks do not certify current-main images,
managed providers or full release acceptance. PRD-03, PRD-05 and PRD-60 remain
open at **8%**, **14%** and **18% estimated work remaining**, respectively,
unchanged planning estimates. Large-load testing remains disabled until after
the operational MVP milestone.

## Temporary Board scope-read recovery (2026-10-16)

Board history previously treated every non-200 Board scope response as a
permanent unavailable administration scope. A temporary 429/500/502/503 now
clears private display authority and shows the existing generic retry error with
Refresh invitations available. Every later read still checks the current
account, actual Board head, matching active Board and administrative permission,
history shape and final account before publishing rows. Confirmed
401/403/404 or invalid/nonadministrative 200 scopes retain private denial.
Background live/account rechecks remain independently authorized; this change
does not require a user action before an already scheduled fresh read.

All five new component cases fail against the original source and pass after
the repair. They cover four temporary statuses, zero history reads or revocations
before admission, explicit Refresh recovery and a subsequent nonadministrative
scope that still withholds history. The complete focused history/issuer/telemetry
selection passes **128/128**. No backend, schema, deadline or mutation policy
changes are involved.

The native browser fixture keeps the simulated scope outage active throughout
its privacy assertions, then restores the real endpoint before keyboard Refresh.
Its first version restored service after one request and failed both viewports
because competing live admission reads could already recover. That observation
does not establish a product privacy failure: successful disclosure followed
a fresh real authorized scope check. The corrected fixture retains fresh-head,
keyboard, accessibility, exact single revocation and canonical-state assertions.

PRD-03 remains open at **8%**, PRD-04 at **15% estimated work remaining**.
Current exact-commit immutable CI and all remaining acceptance criteria still
govern closure. Large-load testing remains disabled until after the MVP.

The corrected complete desktop/phone file passes **2/2 in 88,791.274 ms**,
with no failures, flaky/skipped cases or report errors. It checks the private
outage display, fresh real protected history, keyboard Cancel with zero DELETEs,
keyboard confirmation with exactly one DELETE, one canonical revoked invitation,
one document navigation and zero WCAG 2.2 violations. The fixture runs current
frontend and read-only compiled API/Worker against fresh PostgreSQL 17.11,
149 migrations and restricted roles. Production runtime uses controlled mail
and optional verification; this does not certify managed providers, strict
verification, retained current images or full release acceptance.

All **1,276 source, 71 binary, four served frontend asset** and private Compose
hashes match. API identity/start remain stable; the original scoped Worker
helper restores the same cached image healthy and unscoped. Original Docker
inventory is restored and **3,329 temporary bytes** removed after success,
without building, downloading or importing images. The earlier failed fixture's
containers are removed, but automatic approval review blocked cleanup of its
remaining private diagnostic folder; that deletion was not retried. Web/browser
types, lint, all **285 CI coverage guards**, 1,937 documentation links and
594 anchors pass. The repository load-test flag is independently confirmed **0**.
