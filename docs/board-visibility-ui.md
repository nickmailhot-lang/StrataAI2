# Board visibility administration

## Executed desktop and phone visibility recovery

Both `board-visibility.spec.ts` cases now pass locally against Production API,
restricted schema-110 PostgreSQL, scoped separate Workers and the fresh
production web bundle behind current Nginx/CSP. Keyboard selection, initial
Cancel focus, cancellation return focus, stale-version conflict, explicit
renewed consent and canonical read-only recovery after a lost Public response
pass. A separate anonymous browser changes from private denial to public
read-only admission with no edit/admin controls, and its visibility write is
rejected. No horizontal overflow is observed at 1280px or 390px.

The initial phone run reopened review after an HTTP refresh but before the
competing original Worker event reached its live connection. That invalidation
correctly retired consent. The fixture now matches the canonical visibility
event ID/revision to the genuine upstream frame and waits for the subsequent
successful Board read before opening fresh review. Publication readiness alone
does not establish client consumption. Product consent rules, assertions,
timeouts, zero retries and production limiters remain unchanged.

This is scoped local Production evidence with frozen API/Worker assemblies in
cached runtime images and the ordinary unverified-account browser policy.
Retained-current-image acceptance, full accessibility, role/operation/parent
matrices and capacity remain required. See the complete
[PRD-05 acceptance audit](architecture/prd-05-acceptance.md).

## Visibility contract

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

Live visibility administration now uses the shared authorized Board stream and polling fallback. An invalidation cancels open consent, clears the old Board details, and reloads current administration and visibility. Invalidations during a read coalesce into a subsequent read. Current denial clears metadata and stops the subscription; transient read failures schedule a bounded, read-only retry. Uncertain writes are never automatically repeated. Dialog exit restores focus to the review action or the refresh button when the old action is gone.

Component coverage adds live stale-consent cancellation and current-authority revocation. The release browser fixture now introduces its competing visibility change after the reviewed request is submitted, so it still exercises server version rejection with live updates enabled. Browser collection succeeds; execution against the exact release images remains pending. This increment does not complete PRD-05 or its full performance, accessibility, and realtime acceptance criteria.
Automatic live recovery retains a prior conflict/uncertain-write warning until an
explicit check or another reviewed command. Loading current visibility never
creates a mutation acknowledgment. When live invalidation removes the old review
action or makes it disabled, dialog closure restores focus to current visibility
refresh, waiting for an outstanding read if necessary. Component tests cover
conflict retention and focus restoration. Required release collaboration coverage
now includes a separate desktop/mobile administration scenario and explicit
per-Organization Worker scope; collection passes, exact-image execution pending.
