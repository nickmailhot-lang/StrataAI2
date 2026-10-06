# Board archive review and recovery

PRD-04 BOARD-FR-008 and PRD-18 use an explicit MUI review from the Board screen. A current administrator can archive an active Board with a valid canonical revision. The dialog explains read-only state, retained Lists/Cards and restoration from the Organization archive directory. New archive commands are unavailable on archived Boards. The server validates current administrator admission, active lifecycle and reviewed version.

The original command key, Board identity and reviewed revision remain after a timeout, transport failure or malformed acknowledgment. Recovery can continue after a fresh snapshot makes the Board read-only, because administrator admission is distinct from editing permission. The client accepts only an archived acknowledgment with the reviewed scope/name and incremented version. Conflict requires another explicit current review. Known denial or fresh administrative/scope withdrawal retires pending work and ignores its late acknowledgment.

Board recovery participates in the screen's existing command exclusion: other mutations cannot proceed while the original archive is unresolved. The dialog announces fixed outcomes and restores focus to the archive action or Board refresh. Archived Boards expose an authorized archive-management link for restoration or deletion.

The existing bounded telemetry pipeline adds the fixed board_archive action for review open, use/retry, duration/outcome, conflict and exceptions. No IDs, names, revisions, command keys or server diagnostics are retained. Organization discovery now excludes archived Boards in both stores; the separate archive directory retains current administrative filtering. API source coverage checks archive receipt replay on a read-only Board and active-directory removal/reappearance. The exact-image PostgreSQL discovery fixture checks archive-directory inclusion and active-directory removal/restoration.

Eight control cases pass for reviewed acknowledgment, same-key read-only recovery, changed revision/conflict, HTTP admission denial, fresh administrator loss with late acknowledgment and missing revision/archived new-command refusal. Their actual telemetry payload is checked for private material. A BoardScreen integration case passes for unresolved archive command exclusion, fresh read-only snapshot, original retry and focus return. The existing 34 Board screen/drag cases also pass. The combined Board archive/directory/Organization suites have 27 passing cases. Web type checking, lint, production build, script syntax and full solution compilation (zero warnings/errors) pass.

The serial release browser suite includes board-lifecycle.spec.ts at 1280px and 390px. It uses the existing real Worker scope, two live Board clients, keyboard activation and deliberately lost successful archive/restore/delete responses. It checks identical request recovery, active-directory removal, current read-only reconciliation, explicit irreversible consent, focus return, untouched active/archived child states, deleted parent denial and empty-directory persistence after reload. It runs against the immutable images through the existing full-suite CI step. Browser TypeScript checking and discovery pass; those results do not prove native execution.

Native desktop/mobile/keyboard lifecycle execution, complete concurrent/cross-surface/performance coverage and fresh API/PostgreSQL runtime execution remain pending rigorous CI. This increment does not complete Board management or lifecycle acceptance.

The native lifecycle scenario now waits for each destructive confirmation to be
enabled, dispatches Enter to that control, and verifies that exactly one first
request reached the intercepted route before testing lost-response recovery.
This separates a keyboard submission failure from a missing recovery control;
the same-key retry, two-client propagation, retained child state, and focus
assertions remain required. Browser TypeScript checking passed. The earlier
`96394b9` exact-image run failed the mobile restore retry check and desktop
archive-directory focus check; this adjustment alone does not prove either
runtime defect resolved. A subsequent exact-image run remains necessary.
