# Board visibility administration

`/app/{organizationId}/boards/{boardId}/visibility` is linked from active Boards
for administrators. It requires an authorized current Board snapshot with exact
Organization/Board binding, supported visibility and a positive safe version
before exposing the Board name or form. Private, Organization and Public settings
explain read/discovery access without implying edit rights.

Every change requires a confirmation dialog, initially focused on Cancel. Public
confirmation explicitly explains unauthenticated read access. The mutation sends
the loaded Board version and a fresh UUID retry key to the existing server-side
administration command. The UI acknowledges only a response bound to the exact
Board, intended visibility and a newer version, then loads a fresh authorized
snapshot. The server emits the existing `BOARD_VISIBILITY_CHANGED` event.

An uncertain response or conflict clears stale Board details and offers a
read-only current-state check. That check does not repeat the mutation or claim
which actor caused the current visibility. A subsequent change requires fresh
consent against the newly loaded version. Access denial clears protected data.
Navigation aborts requests; a 15-second deadline also bounds response parsing.
The screen stores no Board details in browser storage.

Component cases cover explicit confirmation/versioned mutation, foreign scope
denial before disclosure and conflict recovery without another write. Release
browser, two-client realtime, accessibility and performance evidence are still
required. This increment does not complete PRD-05; member management controls and
other ticket acceptance criteria remain outstanding.

`board-visibility.spec.ts` adds mandatory release-browser scenarios at 1280px and
390px. They use public API setup and keyboard controls, verify cancellation and
focus restoration, change visibility from a competing client after the form
loaded, and require stale-version rejection before read-only recovery. They then
drop a successful Public acknowledgment and recover the canonical state without
another write. Anonymous reads must transition from private denial to public
read-only access, while anonymous visibility mutation remains unauthorized.
Local collection passes for both cases; exact-image execution is pending CI.
