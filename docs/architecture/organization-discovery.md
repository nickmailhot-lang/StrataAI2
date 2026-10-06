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
membership or organization owner/admin status. Only active Boards appear in this
directory; archived Boards use the separate current-administrator archive directory,
and deleted Boards are excluded. Restoring a Board makes its active entry reappear.
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

Board grants currently require active organization membership when assigned;
the same requirement now applies when using stored board grants. Removed or
suspended organization members cannot retain board edit/admin/move rights through
an old board role. Public active boards retain anonymous read-only access. The
tests attempt protected reads and writes with persisted board-admin rows after
organization revocation. Future board-only guest invitations require their own
explicit eligibility contract rather than bypassing revocation of internal members.

These features remain partial ticket progress. Invitations and member management
UI, organization settings/lifecycle, board settings/lifecycle/copy, pagination and
the remaining PRD acceptance criteria are still outstanding. Live permission
updates require the planned realtime connection; every API request remains
independently authorized now.

## Bounded Organization directory API

`GET /organizations/directory?after={uuid}` returns `{items, nextCursor}`.
Omit `after` for the first page. Cursors require a nonempty UUID in canonical
hyphenated form; invalid input returns `invalid_organization_cursor`. Responses
are private/no-store and require a current authenticated account.

Each page visits at most 50 membership routing hints, with one lookahead hint.
Every visited Organization is independently admitted through the canonical
Organization read transaction before metadata or role is returned. Removed
memberships and inactive Organizations are omitted. A page may therefore be
short or empty while still providing a continuation cursor. Advance using
`nextCursor`, even for an empty page, until it is null; do not infer completion
from item count. The cursor tracks the last visited hint, so omitted entries do
not cause repeats or prevent forward traversal. Current account checks run
before traversal and before returning the assembled response. Admission/storage
failures other than protected not-found discard the page rather than exposing
partial results.

The API-host fixture seeds 52 routing candidates and checks bounded traversal,
removed/deleting omission, complete nonduplicated continuation, foreign-account
isolation, private caching and invalid cursors. Compilation is source evidence;
native API-host and PostgreSQL execution remain required. The existing `GET /organizations` legacy array response remains for older clients.
Browser discovery now uses the paged contract described below. Production
large-directory evidence and full PRD-03 acceptance remain unfinished.

The mandatory exact-image `test-organization-member-directory.sh` fixture also
checks the paged contract through Nginx against PostgreSQL and the restricted API
role. It seeds 53 membership hints, omits one removed and one deleting candidate,
and compares both pages with independently queried active membership/Organization
IDs. It requires 51 unique results and terminal continuation, rejects malformed
cursors, excludes Portal-only Organization access, and includes the paged read in
the existing observed database lock-wait/session-revocation scenario. Shell syntax
and diff checks passed locally; these production runtime assertions await CI.

The API-host directory regression also retains 50 lower-sorted removed membership
hints. It requires an empty first page with a non-null cursor, no revoked
Organization name, and continuation into the same later authorized page. This
covers the distinction between an empty page and terminal traversal; browser
consumers must preserve that distinction when migrating to this contract.

The exact-image PostgreSQL fixture separately registers an account with 51
Organization routing hints ordered by UUID: 50 removed grants and one later
active grant. Its first HTTP page must be empty but resumable and contain no
Organization metadata. Continuation must return exactly the independently
queried final active Organization with Member role and a terminal cursor. Each
seeded Organization retains a separate active Owner. Shell syntax checks passed;
current-image runtime execution is still pending.

Before native pagination fixtures seed larger directories, the exact-image check
hashes complete Organization and membership rows plus scoped audit counts for
the owner/member/Portal directory reads. It requires identical state afterward,
covering read-only version, metadata and audit behavior in the production provider.
This assertion awaits native CI along with the other directory scenarios.

Directory routing database failures use `organization_storage_unavailable` (503)
without a partial page or provider details. The specialized Infrastructure read
boundary preserves cancellation and the separate runtime schema/role refusal
contracts. Existing internal search traversal retains its own contract. The
exact-image fixture temporarily denies the restricted API route-table read,
requires the stable masked error, restores the grant, and requires a successful
fresh directory read. Cleanup also restores the fixture grant on failure.
Native runtime execution remains pending CI.

## Browser paging and direct Organization admission

The global `/app` directory displays one page of at most 50 Organizations.
**Next Organization page** replaces the displayed page; **First Organization
page** resets traversal and remains available after a failed later page. An empty
nonterminal page explains that later Organizations can still be checked and
keeps continuation available. Page changes withdraw old names immediately and
close creation consent; pages are not accumulated in browser state.

An Organization deep link reads `GET /organizations/{id}` directly, verifies the
returned ID and active state, then loads its Board directory. It does not scan
Organization pages or reject access merely because the Organization is beyond
page one. Profile checks before and after the protected read preserve account
binding. The complete read has a 15-second deadline, aborts on scope changes,
and ignores late canceled responses. Malformed, duplicate, over-capacity and
nonadvancing-cursor pages are refused before their names are displayed.

Component coverage retains creation/default-private Board, realtime withdrawal,
late-response, account replacement and scope-clearing scenarios, and adds empty
page continuation, first-page reset, failed-page recovery and malformed page
refusal. The native `organization-directory.spec.ts` creates 51 actual acknowledged
Organizations and checks keyboard paging and a later-page direct deep link at
1280px and 390px. Browser TypeScript checks passed; native execution remains
pending the exact-image CI gate. Source checks do not complete PRD-03 acceptance.

## Bounded active Board directory API

`GET /organizations/{organizationId}/boards/directory?after={uuid}` returns
`{organizationId, items, nextCursor}`. Each page returns at most 50 active,
currently discoverable Board summaries, ordered by UUID, with one lookahead row.
A nonempty canonical UUID is required when `after` is supplied; malformed
cursors return `invalid_board_directory_cursor` (400). Responses are private and
no-store. A null cursor ends traversal; follow the returned cursor to visit the
remaining rows without accumulating an unbounded response.

The owning Organization transaction holds current parent and member admission.
Private Board eligibility and active lifecycle filtering happen before the seek
and 51-row limit in both providers. Member departure, inactive Organizations and
final actor loss refuse the page. A current role change during the read discards
the whole result. Production storage errors use the existing masked Organization
failure contract. Archived Boards retain their separate administrator directory;
the internal search traversal continues to include its existing archived scope.

The API-host fixture covers 52 visible Boards behind more than a page of earlier
private and archived candidates, exact continuation, explicit private membership,
Owner discovery, malformed cursors, outsider denial, revocation and parent
deletion admission. The restricted PostgreSQL contract adds 51 earlier archived
Boards and checks active pre-limit filtering, complete seek, tenant isolation and
preservation of the existing search traversal. Compilation is not runtime proof;
CI execution remains required. The browser and legacy `/boards` array still use
their existing contract pending the browser migration. Full PRD-03/04 acceptance
remains unfinished.
