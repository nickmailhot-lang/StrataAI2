# Persisted board interface

PRD-01, PRD-04, PRD-07, PRD-08 and PRD-09 share the board screen at
`/app/:organizationId/boards/:boardId`, with card details at
`/app/:organizationId/boards/:boardId/cards/:cardId`.

The screen requests the authorized board snapshot from the API; it contains no
sample board or fallback cards. Returned organization and board IDs must match
the route before anything is displayed. Changing either scope remounts the screen,
aborts the previous read and clears its data, drafts and status. Card IDs are
resolved only within that snapshot. Private and unavailable resources use a
generic error; API problem details are never rendered. Correlation IDs support
operator investigation.

Active boards with `canEdit` expose list creation, card creation and title and
description editing. Public/read-only and archived boards omit those actions.
Server authorization remains decisive for every mutation. Requests retain the
same-origin cookie/CSRF transport, and edits submit the current card version.
Work mutations also retain a key for an unchanged submission after an uncertain
outcome, as documented in `work-command-retries.md`.
The UI refreshes authoritative state after acknowledgment rather than claiming
that unsaved data persisted. Conflicts preserve the draft, disable resubmission
and provide an explicit discard-and-load-latest action. Descriptions are rendered
as text. Card links, dialogs, form labels, focus outlines, progress and save status
use the existing MUI components.
Whitespace-only names/titles produce a fixed local validation message without
sending a mutation; name/title controls also enforce the server's length limits.
Failed saves and save announcements are scoped to the card ID, so navigating to
another card cannot inherit its conflict or confirmation state.

`BoardScreen.test.tsx` checks authoritative/read-only rendering, scope mismatch,
scope transitions, acknowledged creation, input validation and conflict recovery
between and within card editors. The browser test
`tests/browser/board.spec.ts` creates a disposable account through the API, then
creates organizations, boards, lists/cards and edits through the UI. The complete
flow runs at desktop and phone sizes, with keyboard activation of primary
creation/save controls on the phone. It verifies persisted
reloads, two-browser conflicts, direct card URLs, back/close navigation, wrong
organization rejection, lost-success-response creation with exactly one card,
and anonymous public read-only access. The normal CI
browser stage runs this test against the already-built release image archives.

This increment does not complete those tickets. List/card movement, lifecycle
controls, copy, Markdown, attachments, realtime updates and the remaining ticket
acceptance criteria still require implementation. Organization/board discovery
and creation are documented in `organization-discovery.md`.
