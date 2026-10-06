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
the previous organization. The requested Organization must pass its canonical current-access read before
Board discovery is attempted. Expired sessions return to
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

These features remain partial ticket progress. The current membership, invitation,
settings, lifecycle and Board administration guides describe their implemented
scope and remaining acceptance work. The browser has current-access realtime
recovery as well as independently authorized requests; bounded directory
behavior is described below. Complete lifecycle processing, retry contracts,
performance/accessibility proof and the remaining PRD acceptance criteria still
require their own current-revision evidence.

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
CI execution remains required. The legacy `/boards` array retains its existing contract for older clients.
The browser uses the bounded contract described below. Full PRD-03/04 acceptance
remains unfinished.

The mandatory exact-image Board discovery fixture now also seeds 52 active
Organization-visible Boards behind 51 private and 51 archived earlier UUIDs.
Together with two existing visible Boards, it requires 54 unique authorized IDs
across a 50-row first page and four-row tail, matching an independent fixture
query. It verifies private/no-store headers, stable invalid cursor failures,
outsider and revoked-member denial, and identical Organization/Board/member
state and audit counts before and after reads. Shell syntax checks passed;
execution against the retained images remains required in CI.

The active Board directory is also included in the mandatory observed parent-row
lock-wait/session-revocation fixture. It revokes the real caller session while
the HTTP request waits for the owning Organization gate, then requires 401 after
the gate is released. This covers current actor admission on an empty directory
as well as a populated page; native execution remains pending.

## Browser active Board paging

The Organization home reads the bounded active Board directory and displays a
single page. **Next Board page** replaces its rows; **First Board page** resets
traversal and remains available after a failed continuation. Pages are not
accumulated. Keyboard focus moves to the available first/next-page control after
loading, or to the heading when no paging control remains. Each page freshly admits its canonical Organization and checks the
same account before and after the bounded request. Paging aborts the old request,
withdraws its names immediately and closes creation consent. Scope changes and
live invalidations preserve the existing cancellation and current-access checks.

The response must match the requested Organization, contain at most 50 unique
canonical Board UUIDs in increasing order, and include positive safe revisions
and valid names. Continuation must match the last returned ID and advance beyond
the previous cursor. Malformed pages are refused before any names are displayed.
A terminal empty later page offers first-page recovery; a first empty directory
retains the create-Board empty state.

Component regressions cover page replacement/reset, failed continuation recovery,
account replacement and malformed binding/UUID/revision/cursor/order/cap refusal,
while retaining live withdrawal, creation, scope transition and direct admission
coverage. The native browser fixture creates 51 acknowledged private Boards and
checks keyboard page navigation and opening the later-page Board at 1280px and
390px. Native execution against retained images remains pending; source checks
alone do not close PRD-03/04.

Local validation of the browser Board paging migration: all 29 Organization home
component cases passed, web source typecheck/lint and native browser TypeScript
checks passed. The complete 1,476-test web suite passed before this migration;
a new complete suite is required for the changed source revision. Native browser
and exact-image acceptance results are still pending CI.

The complete web suite at browser revision `262958d` passed all 1,489 cases in
122 files. This is component/source evidence, not native release acceptance.
A subsequent contract audit found that the directory reader rejected embedded
control whitespace even though the server's existing Board name normalizer allows
it. The reader now follows the same nonblank/160-character name rules and retains
UUID, binding, revision, ordering and cursor checks. A focused regression requires
a server-valid embedded line break to remain discoverable through its canonical
Board link; names are still rendered as escaped React text. The Board link
accessible name collapses whitespace so keyboard/screen-reader naming stays
consistent while preserving the saved display text.
