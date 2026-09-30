# Organization and board discovery

PRD-01/03/04: successful sign-in opens `/app`, which lists the current user's
organizations from the authorized API. New accounts see an empty state and a
create-organization form. Creation opens the returned organization's route at
`/app/:organizationId`, where accessible boards can be opened or created. Board
creation defaults to private; the form also allows organization visibility and
explicit public read-only visibility. It opens the persisted board ID after the
server acknowledges creation. A profile link is available before joining an
organization at `/app/profile`; existing scoped profile URLs remain supported.
The shell links Organizations and Boards to these routes. Unimplemented module
navigation is disabled rather than exposing nonfunctional actions.
At phone widths, compact organization/board navigation replaces the permanent
sidebar, retaining the full width for forms and the horizontally scrollable board.

Scopes remount discovery state, abort in-flight reads and clear data/drafts from
the previous organization. The requested organization must appear in the user's
membership list before board discovery is attempted. Expired sessions return to
sign-in. API errors are sanitized and retain correlation references. Creation
preserves form input on failure and uses the shared same-origin cookie/CSRF
transport. Archived/deleting organizations do not expose the creation button.
Organization role/status serialization retains the existing numeric API contract.

The organization board endpoint now asks the Work Management store for authorized
summaries instead of returning every board name in the organization. It first
requires active organization membership. Public and organization boards are
visible to that member; private boards additionally require active board
membership or organization owner/admin status. Deleted boards are excluded.
The PostgreSQL query runs within the organization RLS session and filters before
returning names. Demo mode uses the same Work Management store that creates
boards, replacing its previously always-empty discovery result.

`OrganizationHome.test.tsx` checks empty state, acknowledged organization/board
creation, default private visibility, expired sessions and scope transitions.
API-host tests cover private/organization/public discovery, outsiders, explicit
board membership, its removal and deleted boards. The browser board workflow
creates both organization and board through the UI before exercising persisted
list/card operations at desktop and phone viewport sizes.
`scripts/ci/test-board-discovery.sh` tests the restricted
PostgreSQL API against disposable membership fixtures, including admin access and
membership revocation. That script refuses to run outside CI and is excluded
from release provisioning; it never weakens runtime roles or the authorization
configuration shipped to users. Build-once CI runs the browser and PostgreSQL
checks against the same image archives used for the release bundle.

These features remain partial ticket progress. Invitations and member management
UI, organization settings/lifecycle, board settings/lifecycle/copy, pagination and
the remaining PRD acceptance criteria are still outstanding. Live permission
updates require the planned realtime connection; every API request remains
independently authorized now.
