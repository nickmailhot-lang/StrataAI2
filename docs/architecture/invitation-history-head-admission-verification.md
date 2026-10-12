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
