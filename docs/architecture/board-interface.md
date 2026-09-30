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
The UI refreshes authoritative state after acknowledgment rather than claiming
that unsaved data persisted. Conflicts preserve the draft, disable resubmission
and provide an explicit discard-and-load-latest action. Descriptions are rendered
as text. Card links, dialogs, form labels, focus outlines, progress and save status
use the existing MUI components.

`BoardScreen.test.tsx` checks authoritative/read-only rendering, scope mismatch,
scope transitions, acknowledged creation and conflict recovery. The browser test
`tests/browser/board.spec.ts` creates an account, organization and board through
the API, then creates lists/cards and edits through the UI. It verifies persisted
reloads, two-browser conflicts, direct card URLs, back/close navigation, wrong
organization rejection and anonymous public read-only access. The normal CI
browser stage runs this test against the already-built release image archives.

This increment does not complete those tickets. List/card movement, lifecycle
controls, copy, Markdown, attachments, realtime updates and the remaining ticket
acceptance criteria still require implementation. Organization/board discovery
and creation are documented in `organization-discovery.md`.
