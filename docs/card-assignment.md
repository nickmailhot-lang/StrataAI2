# Card assignment (PRD-11)

The assignment foundation exposes authenticated
`GET /boards/{boardId}/assignable-members?after=uuid`. It returns Organization
and Board IDs, up to 50 `{userId, displayName}` choices and a UUID seek cursor.
Names may be identical; stable user IDs identify choices. Email addresses,
account state, Organization roles and administrator profile fields are absent.
The existing administrator membership directory retains its separate contract.

Discovery requires current active Organization membership, view permission and
an active Board. An authenticated visitor to a PUBLIC Board has no membership
directory access. Admission precedes cursor validation. Private/unavailable
scope uses `board_not_found`; an admitted invalid cursor uses
`invalid_board_member_cursor`. Each returned user must have active explicit Board
membership, active Organization membership and an ACTIVE account, with verified
email when the configured identity policy requires it. Organization
administrators without explicit Board membership are not assignment candidates.

The PostgreSQL query applies eligibility before the 51-row limit using composite
tenant/Board joins. It uses the owning Board command transaction and existing
runtime privileges. The transaction rechecks admission after lock waits and
verifies the requesting session after the read. The demo provider follows the
same eligibility policy. The fixed `assignable_member_read` telemetry operation
uses existing stable result dimensions and excludes names or user IDs.

Host regressions cover scope, minimal disclosure, active membership/account
eligibility, Board removal, Organization removal, 50+2 pagination, invalid
cursors, anonymous/PUBLIC visitor denial and archived Boards. A required
exact-image fixture additionally reads through the release web proxy with the
restricted API database role and observes Board lock waits before membership
and session revocation. Strict local compilation and fixture syntax checks
passed. Host execution and exact PostgreSQL/runtime execution remain pending
Linux CI; Windows Application Control prevents local host-test execution.

PRD-11 remains open. Account departure cleanup, Card face/detail UI, member filters,
historical attribution, notification suppression, two-client/accessibility
acceptance and documented performance evidence still require implementation.
Historical users and events must remain stable during membership cleanup.

Migration `030_card_members` adds the persisted association structure, timestamps,
positive association revisions and the assigning actor. Composite foreign keys
require the same Organization/Board for Card and target Board member, active or
historical Organization membership rows for target and assigning actor, and a
unique Card/user pair. Forced RLS applies to reads and writes. API privileges
allow the association operations; the Worker receives no association privileges.
Existing membership rows remain stable during soft departures, permitting
history to retain actor IDs. Current eligibility and atomic cleanup are command
responsibilities; the schema alone does not implement assignment or departure
behavior. A required PostgreSQL fixture covers multiple assignees, duplicate and
cross-scope rejection, tenant reads/writes, missing tenant and invalid revisions.
Migration upgrade/repeat and rollback/serialization fixtures now include version
030; API/Worker readiness requires all 30 real migrations and rejects its
absence. Local strict compilation and shell syntax passed; actual database
execution remains pending CI.

Card assignment commands use `PUT /cards/{cardId}/members/{userId}?version=N`
and `DELETE` at the same path, with the existing Idempotency-Key contract. Both
require current edit authority and active Card/List/Board scope. Assignment
also requires current eligible explicit Board membership and configured account
eligibility; PostgreSQL holds target membership/account share locks through the
transaction. Fresh admission and target eligibility precede historical receipt
reads. Ineligible targets return the same safe `card_not_found` envelope. Removal
permits cleanup of an existing departed assignee without disclosing a directory
profile. Versions must be positive and current even for no-ops. A changed
association advances only the owning Card revision; title, description and rank
stay unchanged. No-ops create no extra audit or event. A successful receipt
contains the canonical Card, target user ID, requested assignment state and
whether the association changed; retries retain the original receipt/revision.

Production commits associations, Card revision, audit, `CARD_MEMBER_ADDED` or
`CARD_MEMBER_REMOVED` invalidation event, durable delivery job and retry receipt
in one existing transaction. Removing an explicit Board member also removes
their assignments across that Board (including archived Cards), advances each
affected Card once and emits removal events in the same command. Other assignees
and historical users/events stay intact. Direct administrator database edits are
not supported application operations and intentionally bypass command cleanup;
the disposable lock-wait fixture uses such edits only to test fresh admission.

Three host regressions cover multiple assignees, no-ops, stale revisions, exact
receipts/key reuse, ineligible targets, caller denial, parent archival and Board
departure with active/archived Cards. The required release-image fixture now
also forces audit failures for both assignment and departure, verifies unchanged
state after rollback, checks persisted assignees/events and exact retries, and
verifies other-assignee retention during departure. Compilation and fixture
syntax passed locally; these new command tests still require Linux execution.
The earlier directory/schema commit 55f2ee8 passed Linux .NET host and PostgreSQL
source CI (run 37044554455); its complete release-image gate is still pending.

Organization member removal and voluntary leave now clear that user's assignments
across all Boards in the departing Organization, including archived Cards.
They retain other Organizations' assignments, other assignees and historical
users/membership rows. Each affected Card advances once and emits a removal
audit/event/delivery job inside the existing Organization transaction. The
Organization parent lock excludes concurrent Work commands before they enter
Board/Card/event scope. Restoring Organization membership does not resurrect
assignments. The two host cases cover administrator removal and voluntary leave
across active/archived Boards and a separate Organization. The required release
fixture also forces audit failures during both operations and checks complete
rollback, revision/event changes, retained foreign assignments and historical
users. Local compilation and fixture syntax passed; execution of these new
Organization cleanup checks remains pending CI. Account deactivation uses its
separate global identity transaction and still needs corresponding cleanup.

The assignment/Board-departure command commit 2bb05d7 passed Linux .NET host,
web and PostgreSQL source checks in run 37045729473. Its image/runtime evidence
is still pending; this does not verify the newer Organization cleanup.

Authenticated current Organization members with view permission can now read
`GET /cards/{cardId}/members?after=uuid` for an active Card/List/Board. The page
contains Organization/Board/Card IDs, canonical Card revision, edit capability,
up to 50 assignees and a UUID seek cursor. Each item contains only user ID,
display name, assigning actor ID and assignment timestamp. Current target
Board/Organization/account eligibility is applied before the PostgreSQL page
limit, so an ineligible account cannot occupy a slot or be surfaced even before
account-deactivation cleanup is complete. PUBLIC visitors outside the
Organization and anonymous users cannot access this detailed member read.
Fresh Board admission and final session verification protect reads after waits;
cursor validation follows admission. The fixed `card_member_read` instrument
has no person or Card-content dimensions.

Host coverage checks scoped attribution without email/admin fields, removal,
anonymous/PUBLIC/private denial, archived parents and 50+2 paging after account
eligibility changes. The required release-image fixture also reads persisted
associations through the web proxy, checks 50+2 pages and observes membership and
session revocation during Card-assignee read lock waits. Local strict
compilation and shell syntax passed; these new read checks await Linux execution.
Organization cleanup commit b677ccb passed Linux .NET host, web, PostgreSQL and
source gates in run 37046379129; complete runtime CI is still in progress.
