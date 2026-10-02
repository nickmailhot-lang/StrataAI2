# Permanent Card deletion (PRD-08 / PRD-18)

`DELETE /cards/{id}?version={reviewedVersion}&confirmed=true` requires an archived
Card, current administration, active Organization/Board scope and a nondeleted
parent List. Missing/false confirmation returns `delete_confirmation_required`
after authority/lifecycle checks. Confirmation is part of the deletion-only command
fingerprint; archive and restore fingerprints remain compatible.

Only deletion admission may resolve a Card tombstone. It uses the existing typed
transaction-local routing policy, then current Board/parent authorization before
returning the actor's matching committed receipt. A retry cannot mutate twice.
Changed consent/key fingerprints are rejected. Removed authority, archived Board
or deleted parent cannot disclose the historical receipt. Normal Card lookup
excludes tombstones in both PostgreSQL and the host test store.

Host tests cover consent/lifecycle rejection, identical recovery, changed intent,
irreversibility, outsider denial, Board archive/recovery and parent deletion.
Required restricted-role container checks prove consent rejection without effects,
audit-write rollback, receipt recovery without extra entity/audit/event/job writes
and fresh authority. Local compilation/syntax checks do not establish Linux runtime
acceptance. An active-Card deletion is the established 409
`invalid_lifecycle_transition`, distinct from missing archived deletion consent (400).

The Card archive page now receives current `canDelete` capability with each page.
Contributors retain restoration but see no permanent deletion action. Administrative
deletion reviews the exact Card/parent, states irreversibility and requires a separate
unchecked acknowledgment. It is available under active or archived nondeleted Lists.
Changed Card/parent data invalidates an unsubmitted review. An uncertain response
retains the exact original version/confirmation/key after the row disappears. A
current read that revokes administration aborts/fences pending deletion and removes
its review, while retaining authorized contributor discovery/restoration. A late
response cannot resurrect its intent. No restore-shaped acknowledgment confirms a
deletion. Dialog content disappears immediately on review closure, and focus returns
after exit/current discovery.

Component regressions and desktop/phone release scenarios cover those boundaries,
keyboard consent, lost committed receipt, second-client removal, reload and denied
restoration. Collection is not execution proof. Retention, search/notification
exclusions, telemetry and remaining PRD clauses stay open.

CI run 37031146291 exposed lost focus after a desktop deletion retry. Archive
focus recovery now waits two animation frames after dialog exit and preserves
the return target when a following realtime read disables the focused refresh
button. Pending focus callbacks are canceled on unmount. A new component
regression models native focus loss during that background read and checks
restoration after discovery settles. All 29 archive component tests passed,
as did typecheck, lint and production build. The original desktop/mobile browser
focus assertion is retained; verification of the repair against release images
is pending CI.
