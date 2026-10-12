# Current invitation, comment and cover verification

This audit covers the complete maintained desktop/phone scenarios in
[Board invitation administration](../../tests/browser/board-invitation-administration.spec.ts),
[live Board invitation history](../../tests/browser/board-invitation-history-live.spec.ts),
[Card comments](../../tests/browser/card-comments.spec.ts), and
[Card cover client contracts](../../tests/browser/card-cover-client.spec.ts).
It follows earlier immutable CI failures at revocation recovery, invitation
Cancel focus after a preference update, comment review opening, and cover
candidate opening. Those failures did not reproduce in this local invocation;
no product repair or resolution of their historical causes is claimed.

The added Organization and Board component regressions move focus from Cancel
to Confirm revocation, receive a mocked identity-preference invalidation,
and require the updated timezone caption, the same open confirmation, enabled
Confirm focus, and zero DELETE requests. Existing coverage still requires Cancel
focus preservation, changed-history consent retirement, denied access and
original revocation recovery. A quiet display update cannot invent consent or
submit a revocation.

The final invitation component suite passed **49 tests** without warnings.
The combined invitation/comment/cover suites passed **103 tests** before the
new focus call was wrapped in `act`; the final invitation suite verifies that
adjustment. Existing comment/cover focus tests emitted React `act` warnings in
the combined run. Web and browser type checks, lint, **213 CI guard tests**, and
a fresh full solution build passed.

After the fresh build had completed successfully, the entire four-file native
invocation passed **8 of 8 cases** in **396,861.277 ms**, with zero skips,
flaky cases or report errors. Browser files, scenario budgets, consent, focus,
receipt and persisted-state assertions were unchanged. Independent checks found
zero differences across **2,074 hosted source/runtime files**, **1,409 native
capture files**, and all **four compiled web files**. A preceding preliminary
run also passed, but was started before terminal build success was observed;
it is retained as diagnostic evidence and excluded from acceptance claims.

The runtime used current compiled API and Worker binaries, restricted canonical
PostgreSQL roles and schema 146, and actual Nginx configuration and web output.
Invitation and comment storage, commands and scoped Worker delivery were real.
The cover scenario explicitly simulates publication, receipts and PNG bytes;
its assertion that the canonical Card remains unchanged preserves that boundary.
This is not proof of actual upload/storage/preview-to-cover delivery or retained
immutable release images. General browser-phase account verification policy
applied; it does not replace strict verified-account acceptance.

All owned API, Worker, Nginx and temporary PostgreSQL containers, database, roles
and credential files were removed. The original stopped PostgreSQL container
and data volume were preserved.

Current immutable CI and complete requirements remain necessary for closure.
Planning estimates remain **PRD-03: 8%**, **PRD-05: 14%**, **PRD-60: 18%**,
**PRD-15: 36%**, and **PRD-14: 32% work remaining**. These tickets stay open.
