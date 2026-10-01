# Board member administration

The MUI screen at `/app/{organizationId}/boards/{boardId}/members` links from active
Boards with administration capability. It checks exact Organization/Board binding
and current Board administration before loading a bounded directory page. Every
row must bind to the requested Board, have a known role, positive safe membership
version and ordered unique UUID. Current Organization member profiles require a
name/email; former memberships require null profiles. Malformed rows/cursors are
rejected before any name or email is shown. Pages use the authoritative response
cursor and support previous/next navigation.

Administrators can promote/demote current Organization members and remove Board
membership. Every action opens a confirmation dialog with the Board and person,
with Cancel initially focused. Former Organization memberships expose only a
reference for identification and cleanup, and cannot be promoted from this screen.
The reviewed membership version is always sent as `If-Match` with a fresh UUID
retry key. The server's current authority, version and last-admin safeguards remain
the security boundary. Role acknowledgments require exact Board/user/role binding
and a newer safe version; removal requires 204. The screen reloads authorized
current state after acknowledgment.

Conflicts, safeguard rejection and uncertain responses clear stale private rows.
Read-only recovery does not repeat a write or infer that an absent row proves the
original operation succeeded. Access denial clears Board/profile details. Routes
abort pending work and the 15-second deadline bounds response parsing. No member
data or command intent is stored in browser storage. Confirmation closure restores
focus to the directory refresh control.

Four focused cases cover version-bound consent/role acknowledgment, foreign-row
rejection before disclosure, access loss during removal and lost-response recovery
without another write. Full web/source and exact-image browser evidence, broader
pagination/concurrency/accessibility/realtime checks and performance/telemetry
requirements remain necessary before PRD-05 can close.

`board-members.spec.ts` adds required 1280px and 390px release-browser scenarios.
Setup enrolls an existing account through Organization invitation acceptance,
then grants Board membership through the public API. Keyboard cancellation must
restore focus. A competing role change makes reviewed consent stale; the UI must
send the old `If-Match` and receive conflict. Fresh consent then uses the current
version. Successful role/removal responses are dropped and recovered by reads,
with no repeated mutation or unsupported success notice. After removal, the
recipient must lose private read/edit access while Organization membership remains
identical. Local collection passes for both cases; execution is pending CI. These
cases do not establish realtime delivery, inbox receipt or performance targets.
