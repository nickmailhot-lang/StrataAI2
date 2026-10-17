# Cross-Organization sign-in return verification

Current test source: 1f70dc1028d65a9e7d7075bc166f6cc4c2b7ea2b. Actual unchanged product: fbbe942c5fd3636463318237fe2942ba40c3a6f7.

A genuine owner of Organization A remains denied private Card B before and after explicit sign-in, with stable 404 Board/Internal/Portal reads, no protected target names/navigation/dialog and no protected mutation. Both canonical Boards stay unchanged; browser Back returns to authorized A and opens the correct Card. All original12 sign-in/session/outsider/Portal cases remain and the complete13 passes with zero retries/skips. Current54 unit controls, types/lint,372 contracts and complete coverage pass.

Two current phone captures were inspected separately from automated Axe and keyboard assertions. The original four scoped sign-in-return criteria are now locally verified, and original/new tasks/tests are in Review. The exact combined immutable CI and target-main integration still govern Done. No product code changed; prior build/full2862/0/24 remain actualfbbe942c executions, reused through exact input equality. No parent PRD or MVP is complete.
