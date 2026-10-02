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

Card details now include an on-demand Show assignees section with readable names
and decorative initials avatars. It keeps at most one 50-person page, provides
Next/First controls, and validates scope, canonical Card revision, IDs,
assignment attribution/time, duplicate users and cursor structure before
rendering. A revision, scope or access change retires pending reads and clears
the section; late responses cannot restore stale names. Denials clear names and
show safe local recovery controls, with no raw server error content. These are
read controls; assignment editing and Card-face previews remain required work.
Twelve component cases and 24 Board cases passed locally, along with typecheck
and lint/production build. The required desktop/phone browser collaboration fixture now seeds
an actual self-assignment and opens assignee details with the keyboard. Browser
collection passed; exact-image runtime acceptance remains pending CI.
The backing assignee-read commit 8c961eb passed Linux .NET host, web,
PostgreSQL and source gates in run 37047146982; image/runtime CI remains pending.

Editing now has a bounded `GET /cards/{cardId}/member-options?after=uuid` read.
It requires current Organization membership and edit access on active parents,
returns the canonical Card revision plus up to 50 eligible Board members with
only user ID, display name and assigned flag, and uses UUID seek paging. Target
Board/Organization/account/verification eligibility is applied before the SQL
limit; assignment flags use same-tenant/Board/Card joins. Its own Board
transaction rechecks admission after waits and verifies the session before
returning. Host coverage includes 50+4 choices, assigned/unassigned flags,
verification policy, privacy, invalid cursors and archived parents. The required
release fixture adds 50+2 options through the web proxy, removal flags and
observed Board-membership/session revocation lock waits. Compilation and shell
syntax passed locally; execution of these new checks awaits Linux CI.

The Organization-cleanup commit b677ccb passed the required exact-image
`Bounded Board members, atomic Card assignments and post-wait revocation` step
in run 37046379129. That verifies the command/directory/cleanup fixture as it
existed at that commit; the newer assignee-read/options/UI runtime checks and
the complete required-ci gate remain pending.

Card details now mount an editor outside the conditional canonical Card body,
so an uncertain assignment can still be retried when the Card is temporarily
unavailable. The picker reads one bounded member-options page, validates scope,
revision, eligible option structure, UUID order and cursors, and replaces pages
instead of accumulating names. Named Assign/Unassign buttons submit the current
Card revision and a unique request key; acknowledgement validation checks scope,
member, action and changed/no-op revision. Unknown outcomes retain exactly the
original member/action/revision/key through canonical refresh. Known conflicts
require reloading choices. Admission changes fence late reads/commands and clear
recovery; denied responses hide names and use safe local copy. Other Card
mutations are blocked while recovery is pending. Successful acknowledgement
reloads the Board and restores keyboard focus when the fresh Card is available.

Fifteen picker cases and 24 Board cases passed locally, plus typecheck, lint and
production build. The release browser fixture collected successfully and now
keyboard-unassigns/reassigns on desktop and reads the resulting named/empty
assignee section on a phone through real Worker delivery. Exact-image execution
of the new picker/options/browser checks remains pending CI. Card-face previews,
account-deactivation cleanup, notifications and remaining ticket acceptance
criteria still require implementation/verification.

Card faces now show up to six assignee initials with accessible full names,
Unicode-aware initials, safe unnamed-member fallback, hover names and a remaining
assignee count. The Board snapshot batches previews across active Cards/Lists;
PostgreSQL applies current Board/Organization/account/verification eligibility
before counting and selecting the first six UUID-ordered assignees. Each preview
carries its canonical Card revision. The UI hides a preview when that revision
does not match the displayed Card, during snapshot refresh or after read failure,
and rejects malformed counts/IDs/names. Card navigation keeps the title as its
accessible link name. No additional per-Card requests are issued for previews.

Names and counts follow the detailed assignee-read policy: authenticated current
Organization members with view access receive them; anonymous/PUBLIC visitors
outside the Organization receive `cardMembers: null`. Authenticated Board
snapshots now use the owning Organization/Board transaction, fresh post-wait
view admission and final session verification. Anonymous snapshots retain the
existing PUBLIC read path and contain no member metadata. Archived/deleted
Cards and Cards in inactive Lists cannot enter the preview query.

Host checks cover 52 eligible assignees with six indicators, canonical revision,
minimal member fields, anonymous/PUBLIC visitor privacy and archived-List
exclusion. The required release fixture checks preview counts/revisions/minimal
fields through the web proxy, removal changes and observed Board-read membership
and session revocation waits. The desktop/phone browser fixture also asserts
accessible Card-face initials. Strict .NET compilation, web typecheck/lint/build,
shell syntax and browser collection passed locally. New Linux/runtime acceptance
checks remain pending CI. The preceding picker commit ba99f6f passed Linux
.NET/web/PostgreSQL/source gates, image build and security in run 37049286520;
its complete container/required-ci gate remains live.
Nine indicator cases and 25 Board cases passed in the final serial local run.
An earlier concurrent run had one archive-recovery dialog/focus timing failure;
the unchanged assertion passed in the serial run. Linux browser execution
remains required evidence for that interaction.

PRD-11/16 now share server member filtering via the Board Cards `members` query
parameter. It matches all persisted eligible assignments, including assignees
beyond the six-entry preview, combines with keyword/labels under ANY/ALL, and
requires current Organization membership to protect assignment inference.
See board-filtering.md for bounds, policy and verification scope. Filter UI and
realtime acceptance remain outstanding. Card-face commit bef50bf passed Linux
.NET/web/PostgreSQL/source gates and image build in run 37050782576; its complete
runtime/security/required-ci gate remains pending.

Account deactivation now clears persisted assignments across every membership
Organization, including inactive membership routes and archived parents. The
internal admission hint read includes all membership roles/statuses, without
changing the public active Organization directory. PostgreSQL locks canonical
Organization parents in UUID order before the actor account/session locks, and
rejects newly discovered unplanned routes after a wait. Owner-continuity checks
still apply only to current active Owner memberships. Removed membership/user
rows remain stable historical references.

After successful account/session deactivation, cleanup removes that user's
associations and increments each affected Card once, retaining other assignees.
It appends Card removal audit/events/durable delivery jobs through the existing
writers. An internal lifecycle-only scope lends the already owning identity
connection/transaction to one previously locked Organization at a time,
restoring RLS context afterward. Borrowed tenant commits are no-ops; nested
command transactions and unscoped identity-to-tenant sessions remain forbidden.
Account/session state, identity audit/event, Card cleanup, work audit/event/job
and the keyed receipt share one PostgreSQL commit/rollback boundary. Completed
deactivation receipts do not rerun cleanup or increment revisions again.
The Demo path uses its existing shared account/Organization gate and supplies
behavioral parity; it is not PostgreSQL rollback evidence.

New keyed/unkeyed host cases cover Member/Admin/removed membership routes,
active/archived Cards and Boards, other assignees, historical rows, physical
removal/no-op checks and exact retries. The required release fixture seeds
assignments across active and removed membership Organizations, fails precisely
at Card cleanup audit insertion after identity state/event writes, and verifies
whole-state rollback before retrying the original key. It also checks physical
removal, revision/event counts, retained users/members and no duplicate changes
on receipt replay. Strict .NET compilation and fixture syntax passed locally;
new Linux host and exact-image execution remain pending CI. Notifications and
the full remaining ticket acceptance evidence remain outstanding.
