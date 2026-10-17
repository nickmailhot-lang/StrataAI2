# Original-account List creation recovery

Independent slice `PRD-07-VS-05` repairs a demonstrated List creation gap without moving or restarting `PRD-07-VS-01`. Original create/rename/reorder, copy/move, lifecycle and watching obligations retain their IDs/history.

Two genuine desktop/mobile probes reproduced duplicate Lists: a contributor created a List, its committed response was lost, and a different authorized Organization owner replaced the browser account. An unchanged request key/body returned another201 and distinct ID. Canonical Board reads showed two Lists. Existing actor-specific receipt namespaces worked; the stale browser command lacked its original actor.

The repair reuses Card admission/focus. Explicit List/Card entry confirms the current profile in a bounded read before showing the editable form. Loading is announced; failed entry permits explicit retry. Route departure and protected-scope/permission loss retire admission. The admitted actor stays with the original command through unchanged retries. List creation adds optional `expectedActorId`, as already used for Boards/Cards. The server compares it with the authenticated principal before mutation and returns existing safe session feedback on mismatch. Legacy omission, domain authorization, tenant/lifecycle checks, schemas/events/bodies, receipt rules and dependencies are preserved.

Creation feedback restores focus and scrolls the safe existing Alert into view only while the retired command owns focus. Independent navigation retains focus. Existing MUI, Board controls, shells and routes are reused.

UXA-01 through08 apply to authorized Board entry, List creation/original-key recovery, canonical persisted/reloaded List, then Card creation/open/reload/return. Required evidence includes320/390/768/1280px, persistent200% tablet text, keyboard/focus, unobscured return, no page overflow and applicable Axe. Rendered developer review is separate from automated tests and release/product acceptance.

Material assumption: explicit form entry admits a new command under the current confirmed account. A later account change never refreshes the original actor or silently executes the old command as its replacement. A new authorized form may start a new command. No automatic replay is introduced.

Source389c70abd820bf617c33bd9cfc18f5c04d7c9f07 passes55 focused UI checks (List8/Card7/Board40). Initial API5fail/1legacy-pass and UI7fail/1read-only-pass at249cc083 remain preserved. Current API/native/full regression, rendered UX review, immutable release, review and main integration remain required. No slice/parent Done claim.


Separate visual review found a real local-scroll failure after the original27 browser cases passed: at persistent200% tablet text, a long focused Card extended below its List viewport. The stronger four-width check at8380722a reproduces3passes/1tablet failure. Required task `PRD-07-VS-05-T-02` preserves that failure and adds a minimal reveal of the current return target through its existing scroll ancestors. Sourcead9e826f is undergoing new UI/native/full validation; AC04 and UX acceptance remain open until corrected rendered evidence passes.
