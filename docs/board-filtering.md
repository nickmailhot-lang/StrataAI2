# Board filtering (PRD-10 / PRD-16)

Global search traversal is being implemented separately from Board-local
filtering. The store now provides UUID-seek pages of Organization membership
routing hints and visible Boards, each capped at 51 rows (50 plus lookahead).
The existing full directories remain available for their existing callers.
Routing hints include inactive memberships and are never an authorized search
response. A search coordinator must freshly admit each Organization and Board
in its owning tenant transaction before reading or exposing Card content, and
must verify the actor again before responding. Private Board eligibility applies
before the Board page limit. Archived Board candidates may appear in traversal;
the requested search lifecycle scope must be enforced at content admission.

These internal methods support the global-search API and MUI page below.
The cross-Organization cursor codec uses ASP.NET Data Protection with a distinct
versioned purpose. It binds the actor, normalized keyword/label/member strings,
ANY/ALL mode and active/archive scope to the Organization/Board/Card seek
position, expires after 15 minutes, and caps decoding input at 4,096 characters.
Positions are routing state, never proof of authorization; every resumed content
read must recheck current admission. Account changes or changed criteria reject
the old token. This codec is registered in both runtimes and connected to the
coordinator/API. Remaining acceptance includes runtime authorization, database,
reconnect, accessibility and large-data evidence.

The scoped search store now matches case-insensitive literal substrings against
Card title/description, active assigned label names and currently eligible
assignee display names. A member must have active Board and Organization
membership and an active account (verified when required). ANY combines selected
dimensions with OR; ALL requires each selected dimension. Empty dimensions are
ignored; no selected dimensions lists the admitted lifecycle scope.
Deleted Cards, Lists and Boards are always excluded. Active scope requires all
three parents active; archive scope includes an archived Card or an archived
List/Board ancestor. PostgreSQL evaluates all predicates before UUID seek and
the 51-row limit, under the owning Board transaction. The coordinator uses fresh
Board read admission before invoking this store; the store never replaces
fresh Board admission or actor verification.

The Application Board-search read now holds the owning Board read scope,
freshly admits view access before validation/content reads, and verifies the
actor again before returning. Results include canonical Card/date fields,
Board and List names, and up to 50 active labels/eligible assignees with explicit
overflow flags. The document source kind is currently CARD; future comment
projections can use a distinct kind instead of overwriting Card descriptions.
An explicit archived search remains a read under view admission; it grants no
archive, restore or edit permission.

The global coordinator now traverses membership routes and visible Boards in
UUID order, re-admits each Organization (including cursor resumes), then uses
the Board search read for content. Board search requires current Organization
membership as well as Board view access. Each response contains up to 50 Cards
from one Board; its opaque cursor continues through that Board and subsequent
Boards/Organizations. At most 20 Organization admissions and 20 Board content
reads occur per request. Empty responses may carry a continuation; clients must
keep that continuation available instead of declaring a final empty result.
Exhaustion is reported only by a null cursor. A final actor verification fences
the whole response. This is a sequence of authorized tenant reads, not an atomic
cross-tenant snapshot; resumed pages always use current admission and data.
The coordinator is connected to GET /search and the internal MUI page.

The authenticated API now exposes
`GET /search?q=...&label=...&member=...&match=all|any&scope=active|archived&after=...`.
Criteria are trimmed and capped at 160 characters each; names are literal
case-insensitive substrings, not a query language. Defaults are empty criteria,
ALL and active scope. The actor always comes from the current server session.
Invalid criteria, scope, composition or cursor return `invalid_search` (400)
after actor verification; unavailable sessions return 401. Responses use
`private, no-store`. Clients must URL-encode the opaque continuation, keep the
same criteria, and allow an empty continuation page until `nextCursor` is null.
Runtime/acceptance evidence remains outstanding.

The internal shell now links to `/app/:organizationId/search`, with MUI text,
label/member name, composition and lifecycle controls. A submitted search
replaces its current page rather than accumulating an unbounded collection.
Results show Board/List, label/member previews, deadlines and canonical Card
links. Empty continuation pages retain a Next action. Pre/post-response profile
validation and request retirement fence late or other-account content; account
changes clear query drafts and results. Focus, online/visibility recovery and a
10-second foreground refresh re-read the current submitted page; the global
page does not claim a cross-Board SignalR subscription. Seven parser/component
contracts passed locally; typecheck/lint passed. Native keyboard/mobile/Axe,
runtime/SQL and exact-image acceptance remain pending.

Search transient-read recovery retains the admitted actor and current cursor
while withholding all result content. Online/focus recovery can then re-run the
same submitted page with fresh pre/post-account checks. Terminal 401/403/404 or
an account change purge actor, cursor, submitted criteria and drafts. Eleven
focused parser/component cases passed locally, including offline-read recovery
and each terminal denial; typecheck/lint passed. This mocked component recovery
is not native browser/realtime acceptance evidence.

The required release browser suite now includes desktop (1280px) and phone
(390px) global-search scenarios: real registration/session, two Organizations,
private Boards, assigned labels/members, keyboard form/pagination, canonical
links, actual API Card edits, foreground refresh, offline failed read followed
by online recovery after real scoped Worker delivery, and WCAG Axe checks.
The independent mutation writer is APIRequestContext, not a second browser
editor. Both cases are discovered locally; actual exact-image execution remains
pending. Discovery does not prove keyboard, accessibility or live recovery.

The mandatory restricted persistence executable now also invokes the search
content contract: 52 matching Cards plus an earlier nonmatching Card, literal
description/label/member criteria before the 51-row lookahead, 50+2 UUID seek,
ANY/ALL, removed assignee membership, deleted label, archived List scope and
rejection outside the owning transaction. Seed and cleanup use trusted fixture
IDs; actor admission is synthetic/null and does not prove HTTP authorization.
Compilation passed; real PostgreSQL execution remains pending.

Authenticated viewers of an active Board can read
`GET /boards/{boardId}/cards?keyword=...&labels=uuid,uuid&match=all&after=uuid`.
The server read supports keyword, label, eligible member, due-completion and
deadline-state and recent-update predicates. Anonymous PUBLIC Board parity,
and global search remain required PRD-16 work.

`activity=day/week/month` selects canonical Card `updatedAt` at or after the
server's captured instant minus 1/7/30 rolling UTC days. `month` means 30 days,
not a calendar-month boundary. Omission/`activity=all` adds no predicate. Recent
activity here means persisted Card revision changes, including child commands
that advance its revision; Board label-definition changes alone do not change
this timestamp. The query does not inspect protected activity bodies or derive
state from telemetry. This dimension composes under ANY/ALL before pagination,
uses the same clock as deadline filters, and persists via Recent Card updates.
Each page remains a live read at its own server time.

Host coverage checks fresh/3-day/14-day/60-day Cards, all windows, composition,
safe input admission and parent archival. The required release dates fixture
uses its genuinely changed Card plus the explicit older metadata fixture to
check each window, ANY/ALL, denial and unchanged command effects. Component
coverage checks persisted criterion restoration and invalid saved input. New
Linux host/release execution remains pending.

Desktop/mobile native deadline scenarios now use an independent real API writer
and subscribed browser with the scoped release Worker. Upcoming results appear
after scheduling, disappear after completion, return after reopening, and
disappear after a past deadline is delivered while the browser is offline.
Reconnection must recover without manual reload. Keyboard selection of overdue
and recent-update criteria, persisted canvas reload, deadline removal, canonical
version/state and WCAG checks are included. The existing two-browser label
scenario now supplies its second context's verified release origin explicitly.
All three scenarios are discovered; native execution remains pending.

`due=none` selects Cards without a deadline. `due=overdue` requires an incomplete
deadline strictly before the server's current UTC instant; `due=upcoming`
requires an incomplete deadline at or after that instant. Upcoming includes all
future deadlines, with no hidden horizon. The Application captures one clock
value per read and both stores apply the same comparisons before pagination.
The captured value uses PostgreSQL microsecond precision to preserve the same
strict/inclusive boundary in the demo and persisted stores.
Date-only deadlines already retain PRD-12's timezone-aware end-of-day UTC
normalization; filtering never treats the client timezone as authoritative.
Omission/`due=all` adds no predicate. The dimension composes under ANY/ALL and
persists with the MUI Deadline state selector. Invalid admitted API input returns
`invalid_board_filter`; inaccessible Boards retain the ordinary safe refusal.
The directory remains live: each page is evaluated at its own server time.

The deadline increment has 31 passing filter component tests, passing typecheck
and lint, and zero-warning solution compilation. New host cases cover absent,
past, future and completed deadlines, ANY/ALL and safe validation admission.
The mandatory release dates fixture covers completed exclusion, no deadline,
overdue and an explicitly seeded UTC-relative future deadline through the exact
restricted API. It removes its own future fixture after composition checks.
Actual new Linux host/release execution remains pending; local compilation and
fixture syntax checks do not prove that execution.

`completion=complete` selects the canonical PRD-12 `dueComplete` flag;
`completion=incomplete` selects its false value, including Cards without a due
date. Omission or `completion=all` adds no predicate. This does not introduce a
second general Card completion field. Completion participates as one selected
dimension in MATCH ALL/ANY alongside keyword, labels and members. Unknown values
return `invalid_board_filter` only after Board admission. PostgreSQL applies the
predicate before its 51-row limit; the demo store uses identical composition.
The MUI Due completion selector persists under the existing account/Board session
key and is restored for fresh admitted reads. Invalid stored values are ignored.
Host coverage includes composition, no-date Cards, denied validation and archived
parents; the existing mandatory exact-image Card dates fixture checks complete,
incomplete, reopen, ANY/ALL, stable invalid input and unauthorized reads without
effects. Actual Linux/release execution of this increment remains pending.

The native desktop/phone collaboration scenario now applies due-completion with
its selected label, starts empty, completes/reopens/recompletes the real Card
through the dates API and requires the phone result to appear/disappear through
real Worker delivery. The existing canvas reload also retains the completion
predicate. No filter, date or event response is substituted. Native execution
remains pending; successful discovery is not runtime acceptance.

Completion pagination coverage seeds earlier nonmatching Cards and 52 completed
Cards in the host contract, requiring a full 50-row page followed by two unique
matches. The mandatory release dates fixture uses trusted disposable metadata
setup around the genuinely completed Card to require 50+3 completed results
through Nginx/restricted PostgreSQL, then removes only its own setup rows. These
cases test predicate-before-limit and duplicate-free seek; fixture setup is not
proof of 52 date mutation commands. Linux execution remains pending.

Keyword matching is a case-insensitive literal substring of title or description;
SQL wildcard characters such as `%` and `_` are literal. The trimmed keyword is
limited to 160 characters. At most 25 distinct nonempty label UUIDs are accepted.
Labels are matched by ID through current active Board definitions and persisted
Card associations, including assignments beyond the six Card-face indicators.
Missing, deleted, foreign-Board or unassigned labels cannot satisfy a predicate;
their names or existence are never fetched for disclosure.

`match=all` (default) requires the keyword when supplied and every selected label.
`match=any` requires a supplied keyword or at least one selected label. Empty
criteria include every active Card whose parent List is active. Archived/deleted
Cards and archived/deleted parent Lists are excluded. An archived/deleted Board
is unavailable. Reads return canonical Card records with Organization, Board and
List IDs, at most 50 per page, ordered by UUID. A next cursor is the final returned
Card ID; each request seeks beyond it. This is a live directory, not a frozen
multi-page snapshot. Changed criteria must restart from the first page.

Board admission precedes validation errors and the owning command gate rechecks
view permission after waits. Session authorization is rechecked after the read.
Private Board denial remains `board_not_found`; invalid admitted filter input
returns the stable `invalid_board_filter` code. The read uses a tenant-scoped,
parameterized PostgreSQL query with composite joins and an equivalent demo store.
It does not infer membership from client state or truncated display metadata.
Telemetry records the fixed `board_filter_read` operation and stable result codes;
keywords, label selections and Card content are not metric dimensions.

Host regressions cover all associations, ANY/ALL composition, literal/description
keywords, foreign labels, definition deletion, parent lifecycle, denial before
validation, 50+2 pagination and invalid input. The required exact-image label
fixture additionally covers persisted PostgreSQL filtering beyond face previews,
removed assignments, literal keywords, 50+2 pages, archived parents, and observed
Board-lock waits followed by membership/session revocation without result leaks.
Strict local compilation and fixture syntax checks passed. Linux source CI for
aa3e128 (run 37037711541, .NET job 110939925226) passed the complete host suite.
The required label-command step also passed against the exact aa3e128 API image
(container job 110941574917), including full-association ANY/ALL filtering,
literal keywords, bounded pagination and observed post-wait revocation. The
overall job was still running when this evidence was recorded. The later proxy
repair changes this fixture to execute through the release web image; that
additional proxy coverage and the current complete gate remain pending.

Board viewers can open Filter Board Cards to select named label choices and a
literal keyword, compose ANY/ALL predicates, and browse a 50-Card result page.
Results link to canonical Card details and identify the current parent List.
Label choices are separately paged, retaining selected IDs across pages, and the
selection is limited to 25 labels. Results use the last applied criteria; changing
draft fields requires Apply again. A canonical Board refresh (including event
delivery and reconnect recovery) retires pending reads and reloads applied
results and reloads label choices from the first page while retaining selected
IDs. Late responses cannot restore the old page. Reads validate scope,
lifecycle, Card identity/version/rank and cursor shape before displaying content.
Denied reads clear results and refresh Board admission with safe local messages.

Applied criteria persist in sessionStorage under current `/me` identity,
Organization and Board IDs. Reopening the filter dialog restores criteria for an
explicit Apply. Choosing Show this page on Board also stores the canvas mode;
navigation or reload restores that mode only after a fresh `/me` admission and
server filter read. Results themselves are never stored. A different signed-in
identity gets a different storage key. Clear
filters removes the saved criteria. Unavailable browser storage degrades to the
current in-memory draft. Invalid stored criteria are ignored. Anonymous filtering
remains pending: the current dialog requires authentication and explains denial.

Fifteen filter component cases and 24 Board cases passed locally (39 focused tests),
plus typecheck, lint and production build. Desktop/mobile keyboard browser cases
now include ALL empty results, ANY matching, criterion restoration after refresh,
and clearing. Collection passed; execution against exact images remains pending.

Neither PRD-10 nor PRD-16 is complete. The remaining filters, event/reconnect
acceptance and performance evidence must be implemented and verified before
closure.

The required release browser suite now includes a desktop editor and an isolated
phone browser context using a real authenticated session. The fixture scopes the
already-built Worker to its disposable Organization and waits for durable event
delivery before subscribing. An initially empty label filter must gain a Card
after desktop assignment, retain its selected ID while the label is renamed and
recolored, lose the Card after removal during a forced proxied-socket outage,
and gain it again after reconnect and reassignment. Reconnect attempts are
temporarily rejected while ordinary HTTP replay remains available. No event or
Card response is mocked. Board archival must close the filter and reject its
server read. The fixture always restores Worker scope and closes its browser
context. This scenario has collected successfully; runtime execution is pending
CI and is not yet two-client/reconnect acceptance evidence.

The filtered canvas keeps all canonical List columns and their Card ordering,
showing only the current bounded result page. Each result must match its
canonical parent, version, title, description and rank; a mismatched revision
requests a fresh Board read. Pending or superseded pages show no Cards until
rechecked, including after Board events and reconnect recovery. Clear restores
the complete canonical canvas. List operations retain their full List scope,
which the filtered state explains. Reordering is disabled while filtering;
Card details retain the canonical movement controls. The required phone-client
browser scenario also covers canvas projection, persistence after reload,
assignment removal by the desktop client and restoration after Clear. Collection
passed; exact-image runtime acceptance remains pending.

The server filter accepts `members=uuid,uuid` (at most 25 distinct nonzero user
IDs) alongside keyword/labels. In ALL mode every selected assignee, label and
nonempty keyword must match; ANY mode matches any selected predicate. Member
matches query persisted Card associations rather than Card-face previews, with
same-tenant/Board joins and active Board membership, Organization membership,
account and configured email-verification eligibility before matching and the
51-row Card seek limit. Missing, foreign and departed members are unsatisfied
predicates rather than profile lookups. Member predicates require current
Organization membership; PUBLIC visitors outside the Organization cannot infer
assignments from result membership. Existing keyword/label-only reads retain
their current admitted visibility behavior. Validation follows Board admission;
the existing owning Board transaction and final session verification protect
post-wait reads. Fixed filter telemetry contains no selected user IDs.

Host coverage includes 50+2 Card pages, zero/multiple assignees, member/keyword
and member/label ANY/ALL combinations, an assignee beyond six preview entries,
inactive accounts, departed membership, archived Lists, invalid input and PUBLIC
visitor privacy. The required release fixture adds persisted-association matching
beyond the preview, combined predicates, actual PostgreSQL 50+2 Card pages,
validation/privacy and observed membership/session revocation waits through the
release web proxy. Strict .NET compilation and shell syntax passed locally;
execution of these new checks awaits Linux CI. Member selection in the MUI
filter, stored member criteria and two-client filter acceptance remain required
work; this does not complete PRD-16 or PRD-11.

The MUI filter dialog now offers on-demand assignee discovery using the admitted
assignable Board directory. It holds one 50-person choice page, validates scope,
names, UUID order and cursors, supports Next/Reload pages and retains up to 25
selected user IDs across choice pages. Named checkbox controls support keyboard
use; empty/loading/denied states have safe recovery. PUBLIC visitor snapshots
with no member metadata hide member discovery. Board/scope changes retire reads,
canonical refresh reloads an open member page, and pending/disabled states hide
names. Member denials close the filter and request fresh Board admission.

Applied member IDs are sent with the keyword/label/match criteria to the server.
Storage contains only bounded criteria (never names/results) under the admitted
actor/Organization/Board key. Legacy label-only criteria restore with no members;
member UUIDs are validated/deduplicated case-insensitively before restoration.
Member criteria also restore the bounded filtered canvas after fresh identity
admission; Clear removes every predicate and the stored entry. Another signed-in
actor cannot reuse those member predicates.

Twenty-four component cases passed locally, including member paging/cap,
selection persistence, other-actor isolation, canvas restoration, denial and
late-read fencing. Typecheck/lint/production build and browser collection passed.
The required desktop/phone browser fixture now selects an assignee by keyboard
on the phone and checks result disappearance/reappearance after desktop member
removal/restoration through real Worker delivery. Exact-image execution of these
UI/browser checks remains pending. Server commit cc030b6 passed Linux .NET/web/
PostgreSQL/source gates, image build and security in run 37051528948; its complete
container/required-ci gate is still live. PRD-16's other required filters/search
and full acceptance evidence remain outstanding.
