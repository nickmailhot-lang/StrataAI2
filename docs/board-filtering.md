# Board filtering (PRD-10 / PRD-16)

## Current executed evidence

At `e77195d`, [run 37241937689](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37241937689) passed the exact-image global search fixture (private isolation, literal matching, 50+2 UUID continuation, actor/query binding and archived-List scope). Both 1280px and 390px native global-search cases passed: two independently authenticated browser contexts, cross-Organization contextual results and keyboard pagination, actual MUI Card edits, foreground refresh, offline disclosure withdrawal, online recovery after scoped Worker delivery and Axe checks. Both native deadline-filter cases and the two-browser label/member filtering case also passed. The release failed other native cases; this evidence does not establish a green release or complete PRD-16 acceptance.

The same run retained `search-capacity-e77195d81c018db6631c4f0e69baddc23dce7cbc`, artifact `11318292455`, SHA-256 `849211625cb03cdc4b8aa7508618485d5def38f37a4545d0410405622f2de327`. The inspected report confirms 200 Lists, 5,000 active Cards and 100,000 archived Cards; active/archive first and seek pages each contain 50 matching persisted IDs, with no overlap and unchanged read state. Twenty serial warm samples per page yielded p95 206.047/210.616 ms (active first/seek) and 207.680/206.544 ms (archive first/seek), under the unchanged 500 ms engineering read budget. This is one-client bounded-page evidence, not concurrent-load or native large-result rendering proof.

The native global-search cases now additionally withdraw the actual reader session while results are visible, require automatic foreground withdrawal of result links/continuation and all three criteria, require HTTP 401, and compare the independent editor's full Board snapshot before/after. Fresh login and explicit search must recover without navigation or reload. Browser TypeScript and collection pass; this new extension still requires real-image execution. Private `SEARCH_EXECUTED`/`BOARD_FILTER_CHANGED` producers and canonical acknowledgment consumers are implemented. The new Board Apply/Clear browser integration has local test proof; native canonical/recovery assertions still require exact-image CI. PRD-16 remains open, estimated 18% remaining.

Anonymous PUBLIC Board filtering now uses the owning tenant read transaction with a null actor, locked active Organization and PUBLIC active Board, and admission revalidation before and after content selection. The shared public read gate also retains its existing Board image consumer. Bounded Card filters and label choices admit public keyword/label/completion/deadline/recent-update criteria; member predicates still require current Organization membership, and anonymous label choices grant neither editing nor deletion. PRIVATE, archived or unavailable parents remain concealed before input validation. Global search still requires authentication.

The MUI filter admits an anonymous identity only after HTTP 401 and an explicitly PUBLIC snapshot with withheld member metadata. Its criteria use a separate anonymous session-storage key; another actor's criteria are not restored, and saved anonymous member predicates are stripped. Pre/post-account checks fence anonymous label/result reads, and a newly signed-in account retires pending results. Desktop/phone native scenarios cover keyboard keyword/label filtering, hidden assignee choices, filtered canvas restoration after reload, Axe and automatic withdrawal after the owner uses the real visibility command, with unchanged child state. The required label release fixture also checks anonymous 50+2 exact persisted IDs, read-only choices, member nondisclosure, unchanged protected state and an observed public-read lock wait followed by PRIVATE visibility withdrawal. All 35 filter component cases, web/browser TypeScript, lint, production build, shell checks and strict .NET compilation pass locally; new API/native/restricted release execution remains pending.

## Implementation history

Current anonymous lifecycle extensions remain pending runtime verification: the host contract archives/restores the actual PUBLIC Board, requires safe denial of Card/label reads while archived and unchanged child state after restoration. The mandatory release fixture separately observes both anonymous Card and label reads waiting on the Organization parent gate, archives that parent, requires a 404 without result disclosure (including malformed input), then requires fresh active-parent recovery. Fixture restoration leaves labels, Card associations/revisions and audit/event/job/receipt effects unchanged. Shell syntax and the zero-warning full solution build pass locally; these checks do not replace real native or restricted PostgreSQL execution.

The implementation notes below record earlier development checks and pending states. The executed evidence above supersedes their collection-only status for the unchanged fixtures at `e77195d`; subsequent extensions remain pending unless separately verified.

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
position, expires after 15 minutes, and caps decoding input at 8,192 characters.
The cap admits JSON-escaped multilingual criteria at all three 160-character
limits before encryption/base64 expansion; it also bounds client cursor parsing.
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

Production Nginx and the Vite development proxy route `/search` to the API.
The required exact-image container stage runs `test-global-search.sh` through
that web proxy: anonymous denial, an outsider's empty private result, literal
`100%_` matching, all 52 persisted IDs across 50+2 seek pages, opaque cursor
binding to actor/query, explicit exhaustion and archived-List scope. This
fixture's execution is pending; shell syntax alone is not runtime evidence.
Server request counts, outcomes, stable `invalid_search` errors and durations
use the existing BoardSharing meter with fixed `global_search` operation tags.
Search text, label/member criteria and cursor material never become metric tags.
The existing bounded client-observation transport also admits fixed
`search_disclosure` and `search_read` categories. It records opens, reads,
explicit refreshes, online reconnects, successful/failed durations and client
exceptions; polling/focus reads do not inflate reconnect counts. Reports contain
only action/kind/count/duration, and the server rejects batches containing query,
label/member, cursor or result-title fields atomically. These observations are
best-effort metrics, never authoritative audit history. Seventeen focused web
cases and strict solution compilation passed locally; Linux parser execution,
native acceptance and large-data search measurements remain outstanding.

The required capacity stage also runs `test-search-capacity.sh` against its
existing 200-List/5,000-active-Card/100,000-archived-Card fixture. Active and
archived first/seek pages must each hold 50 results, collectively match the
first 100 persisted matching UUIDs without overlap, and leave Card revisions,
audit/event/receipt counts unchanged. Twenty serial warm samples per page are
retained in `search-capacity-<revision>` with fixed sizes/timings only. The
500 ms page-read p95 is an explicit engineering budget, separate from the PRD's
mutation acknowledgment target. This fixture does not measure simultaneous
clients, every archived continuation page or native UI rendering. Shell syntax
and composition checks passed; actual scale correctness and timings await the
required build-once image run.

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

The native desktop/phone fixture uses two isolated authenticated browser
contexts: one searches across two Organizations, while the other edits the
selected Card through the actual MUI detail controls and checks versioned API
acknowledgments and fresh Board admission. The reader observes that edit through
foreground refresh, then goes offline while the editor makes another change;
results are withheld during failure and recover on reconnection without a full
reload. Real scoped Worker delivery and Axe checks remain part of the scenario.
Both cases were discovered locally, not executed; this is not yet two-browser
acceptance evidence.

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

## Canonical private interaction event contract (PRD-16 section 14)

SEARCH_EXECUTED and BOARD_FILTER_CHANGED are personal interaction sources,
separate from optional telemetry and shared Board Work history. The new
SearchInteractionEvent Application contract fixes the required canonical fields
and permits only the two source factories. Metadata is an immutable empty
mapping: no query strings, selected members/labels, result identities/snippets,
criteria hashes or content are carried. Global search has null Organization and
Board context because it traverses admitted Organizations; it does not invent a
tenant. Board filter changes require both real context IDs. Each immutable
execution/change source uses its event ID as its entity ID and version 1;
recording it must not increment Board/Card/profile versions or alter another
client's filter criteria. Source clocks are UTC at database precision.

The source envelope now has registered PostgreSQL and process-local Demo append
adapters. Successful `GET /search` pages now produce and return their private
execution source. Authenticated Board Apply/Clear intents now use the private
retry-aware producer described below. Current-account fences and original-key
HTTP recovery are implemented; full native/reconnect acceptance remains pending.
Anonymous filtering must not fabricate an actor or emit an authenticated source.
The identity account-event allowlist and shared Work stream remain unchanged.
Two contract tests cover canonical serialization, absence of fabricated
tenant/content metadata and invalid scope/identity refusal. Domain tests passed
in CI for the source contract. The full .NET build passes with zero warnings/errors;
new runtime tests execute in Linux CI because local application control prevents
.NET test execution. Native proof of emitted and consumed domain events remains incomplete; PRD-16 stays open, estimated 18% remaining.

Migration 076 adds actor-private search interaction streams and immutable source
rows with forced subject RLS, paired tenant/Board foreign keys, fixed canonical
entity/version semantics and empty-only metadata. A hardened append capability
checks the current subject/active account and locks actual active Organization,
Board and required membership tuples before a Board-scoped append. Original IDs
are reused only for an identical source; replay does not advance its counter or
clock. API grants are SELECT plus this narrow capability, with no raw table
writes or Worker capability. The PostgreSQL adapter requires an owning identity
subject transaction and neither starts nor commits it. Both runtime adapters are
registered through Work Management; the global search producer calls them after
successful traversal. Board filter change-intent HTTP/client wiring remains pending.

Startup now requires schema 076. Populated forward/repeat upgrade checks retain
original account and shared Work source bytes and require empty new history.
Required restricted SQL fixtures cover forced actor isolation, current private
MEMBER admission, foreign actor/private nonmember and withdrawn-grant refusal,
canonical identity, duplicate/changed-source behavior, counter rollback and
immutable administrative history. Full zero-warning .NET build and migration
runner shell syntax pass. Migration and restricted SQL capability checks passed
in [run 37272101256](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37272101256),
including forward/repeat upgrades, forced RLS, original-ID deduplication and rollback.
The C# adapter explicitly establishes the transaction-local SQL actor only after
checking its owning application subject; it does not depend on an earlier adapter
having set the database context. The mandatory restricted persistence executable
now checks that an unowned/foreign append is rejected, an original source can be
appended twice inside its owning transaction, and a declared late refusal leaves
neither sources nor its newly allocated stream. This C# runtime check passed in
[run 37272879344](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37272879344)
(PostgreSQL job 111643440980); its log includes the explicit adapter success marker.
Actor admission in this contract
is synthetic, so it does not prove HTTP session admission or final session fencing.
The Demo adapter retains immutable original source identities and per-actor
sequences, participates in the owning identity rollback snapshot, refuses foreign
subjects, and freshly checks active Organization/Board visibility and grants
inside the Work read boundary for every Board-scoped append, including duplicates.
It refuses at its process-local capacity rather than evicting originals. A Demo
host test covers original-ID deduplication, changed-clock refusal, late rollback,
private nonmember refusal, actual MEMBER admission, and withdrawn-grant duplicate
refusal. Its zero-warning build and full API-host execution pass in source CI. This test's actor
authorization is synthetic and does not prove HTTP source production.
### Successful global search source production

After a successful authorized and validated `GET /search` page, the endpoint
invokes the registered producer in a separate owning identity transaction. This
preserves the search traversal's existing independent tenant read boundaries.
The producer generates the original EventId and UTC clock server-side, appends
through the private source adapter, and rechecks the original authenticated
session before commit. A final session refusal rolls back the source and counter
and returns no acknowledgment. Storage refusal uses `work_storage_unavailable`;
identity storage failures map to that existing stable API boundary.

The successful page includes `interaction`, containing only the ten canonical
fields above. Every successful page execution, including an empty page or a
valid continuation, is a distinct observation with a new original EventId.
Invalid inputs/cursors and failed admission never invoke the producer. Repeated
GET execution is a new observation; replay of an existing source must reuse its
original identity. There is no criteria, result, snippet, or hash in the source.
Global scope uses null Organization/Board fields and does not mutate work state.

The HTTP host test checks the exact acknowledgment allowlist, authenticated
actor, canonical type/entity/version, null tenant scope, empty metadata, and
distinct original IDs across successful pages. A synthetic final-session refusal
test proves owning rollback by establishing a changed-clock original with the
refused EventId afterward. These tests have executed in passing source CI;
the run-specific evidence below distinguishes source from native release proof.
Full reconnect/native recovery acceptance remains outstanding;
PRD-16 stays open at 18% estimated remaining.

The browser `boardFilterChange` transport/recovery module is now implemented as
the prerequisite for wiring these controls. It creates a frozen normalized
Apply/Clear intent, retains one original under its actor/Organization/Board
session-storage key, refuses replacement of a live original, and caps retained
records at 1,000 without evicting live requests. Restoration validates the exact
record shape, scope, key, query and 24-hour lifetime; invalid/expired records are
discarded without silently allocating another request key. Submission performs
pre/post current-account checks, sends no body, includes the expected-account and
original idempotency headers, and parses/deduplicates the canonical source only
after both checks and cancellation fences. Thirty focused search tests pass,
including lost response/navigation retry, changed accounts before/after dispatch,
foreign-scope acknowledgment, cancellation and storage capacity. Web typecheck
and lint pass. BoardFilterControl now invokes this module for authenticated
Apply/Clear, as described below. Transport tests alone do not prove native UX.

HTTP producer run 37336426132 passed web and restricted PostgreSQL jobs but
failed one API test assertion: the changed-account guard correctly returned
the existing `401 session_unavailable` envelope, whereas the fixture expected
403. Commit `0d96188` corrects that expectation without changing production
behavior or removing the code/no-receipt assertions. The corrected full API suite, web and restricted PostgreSQL jobs pass in
run 37337412135. Build-once images and security checks also pass; container
integration remains live. The browser integration added afterward has local
proof only, and full release acceptance remains unproven.

### Browser consumption of global search acknowledgments

The browser requires the canonical interaction before disclosing a result page.
After matching `/me` reads before and after the response, it checks the exact ten
fields, current actor, original EventId/entity identity, type/version, null global
scope, empty object metadata and valid UTC clock. Missing or malformed sources
withhold results. A source attributed to another actor also clears private query
criteria, continuation and acknowledgment memory, even when both profile reads
match. Aborted, superseded and unmounted reads do not consume an acknowledgment.

The account-specific consumer retains only the latest 1,000 original IDs and
their clocks in process memory. It suppresses duplicate acknowledgment effects
without preventing fresh result reconciliation, and refuses a changed clock for
a retained original. It clears on terminal account/access loss. A newly consumed
source produces the polite, body-free confirmation “Search acknowledged.” No
source identity, query, result content or canonical event is sent to optional
analytics. This bounded acknowledgment window is not durable replay/history.

All 19 focused parser/component tests pass, with web typecheck, browser fixture
typecheck and lint. They cover strict admission, foreign actor refusal, missing
sources, duplicate acknowledgment handling and continued result reconciliation.
The desktop/phone native fixture now asserts the actual canonical HTTP envelope
and its user-visible consumption; exact-image execution remains pending CI.

The producer CI run 37273958250 passed 414 of 415 API tests. Its sole failure was
the extended HTTP fixture reading the continuation stream twice. The fixture now
parses once and reuses the same page for both original-ID and continuation
assertions; neither assertion is relaxed. The complete repaired API-host suite
passed at revision `94988a5` in [CI run 37274882801](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37274882801).

### Board filter producer boundary

`BoardFilterInteractionChangeProducer` is the registered change-intent producer.
It creates a server-owned candidate inside the identity transaction, obtains the
original from retry storage, verifies the original request session, and freshly
re-admits the identical Board source afterward. A final session refusal or Board
grant withdrawal returns no acknowledgment and rolls back a newly appended
source, counter and receipt. Replaying an existing receipt also requires these
current proofs. Board change intents use this retry-aware path. Personal sources do not advance
Board/work versions or enter shared activity history.

Authenticated `POST /boards/{boardId}/cards/filter-change` accepts query fields
`change=apply|clear`, keyword, labels, members, match, completion, due and
activity, plus a nonempty UUID `Idempotency-Key` and the existing CSRF header.
The required `X-StrataAI-Expected-Actor` UUID binds the intent to the account
independently admitted by the client. A different authenticated account is
refused before dispatch; canonical ActorId still comes from server authentication.
It admits the Board and validates filter input before producing a source. Clear
requires empty/default criteria; pagination, unknown query fields and request
bodies are refused. The server derives the private digest from normalized input;
clients never supply canonical event identity, actor, metadata or timestamps.
The response is exactly the canonical ten-field source with private/no-store
caching. A repeated key and normalized input returns the same original. The
existing GET remains a read and has no interaction acknowledgment.

New HTTP tests exercise the real authenticated host, exact envelope, normalized
retry, changed input, independent Clear, unchanged Board state, validation, CSRF
and anonymous refusal. The new synthetic-session producer fixture covers private
nonmember refusal, active MEMBER admission, fresh and replayed final session
refusal, late actual Board-grant withdrawal, and full source/receipt rollback.
These tests execute in the passing full API-host suite for `0d96188`,
run 37337412135. This proves the declared host/session boundary, not native UX.

BoardFilterControl now sends authenticated Apply/Clear through this endpoint
and commits applied criteria only after the admitted canonical acknowledgment.
Uncertain replies retain the immutable original and expose an explicitly named
retry; replacement actions stay disabled. Close/remount restores the actor/Board
original without replacing its key or input. A newly edited draft does not alter
the recovered intent. Canonical acknowledgment and retry restore owned keyboard
focus. Known account/access denial withdraws criteria, directories, results and
pending UI; expiry retires the old request and requires review before another
Apply. Storage conflicts recover an existing original rather than dispatching a
replacement. Storage-provider failure can retain an original in memory only;
remount recovery requires functioning session storage.

Label, member and result reads now fence disclosure with current-account checks
before/after IO. Anonymous PUBLIC filtering stays read-only, with no fabricated
personal source. Restoration, paging, canonical refresh and reconnect are GETs
and do not manufacture change events. The new native desktop/phone label cases
inspect exact canonical sources, drop one committed reply, recover identical
input/key/source, then check independent Apply/Clear originals and zero writes
on reload. The anonymous native case requires zero filter-change POSTs. Native
execution remains pending; two-client/reconnect acceptance still requires the
full release suite.
PRD-16 stays open at 18% estimated remaining.

The browser canonical parser also admits `BOARD_FILTER_CHANGED` originals against
an independently supplied current actor and Organization/Board pair. It requires
the exact ten-field envelope, BoardFilter type, original entity identity, version
1, empty metadata and valid UTC clock; anonymous or foreign-scope acknowledgments
are refused. The bounded acknowledgment consumer compares the complete original
type, actor, scope, entity, version and clock before suppressing a duplicate
EventId. A changed scope or type cannot pass simply by retaining the clock.
The parser is now consumed by Board Apply/Clear through the admitted browser
transport. Focused parser, transport, criteria/result and real-transport component
tests pass locally; native acceptance remains pending.

### Board filter retry storage

Migration 077 adds actor-private retry receipts, separate from the immutable
canonical event and its empty metadata. Each receipt binds a request UUID and
private intent digest to one original event. It retains no criteria, result body
or response JSON. The narrow append-or-replay capability serializes on the actor
stream, checks the active account and current Board grant, and returns the stored
original EventId and clock rather than a retry candidate. Changed intent/scope,
expired receipts and withdrawn grants are refused. A duplicate does not advance
the source counter or its clock.

Receipts have a 24-hour lifetime and a limit of 1,000 per actor. New requests can
remove at most 100 expired receipts in one operation; live receipts are never
evicted to make room, and immutable source events are never removed by receipt
cleanup. An expired request is not replayable; clients must discard expired
retry intents rather than reuse their keys. Runtime API access is through the
narrow capability, without direct receipt writes or Worker grants. Forced actor
RLS, canonical actor/event foreign keys and immutable receipt updates provide
additional storage boundaries.

The registered PostgreSQL adapter borrows the owning identity transaction and
establishes its SQL subject explicitly. Demo uses the same owned actor, scope,
digest, lifetime and capacity rules, with receipts included in identity rollback.
Its inner refusal also restores source/counter/receipt state if a caller catches
the failure. Neither adapter owns the final original-session proof.

Mandatory SQL fixtures cover duplicate identity/clock, changed digest, actor
isolation, withdrawn grants, expiry, expired-only cleanup, capacity and late
rollback. The upgrade fixture applies 077 over existing canonical sources and
verifies their records remain unchanged. The restricted C# adapter contract
checks actual original identity/clock and full source/stream/receipt rollback.
The Demo service fixture covers retry, changed input, owning rollback, grant
withdrawal and expiry while preserving Board version. These storage fixtures
executed successfully in the full source gate at revision `79788f6`,
[CI run 37332882372](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37332882372).
That evidence includes actual restricted C# original identity/clock and full
rollback, ordered upgrade/repeat and SQL capability checks. It does not establish
new HTTP producer execution, browser retry/recovery or native acceptance.
PRD-16 stays open at 18% estimated remaining.


Current Board filter integration validation: 35 criteria/directory/result tests
use an explicitly mocked acknowledgment/account-probe boundary; 11 separate
component tests use the actual same-origin transport and canonical parser with
only declared HTTP response fixtures. The latter cover Apply/Clear originals,
no change writes on refresh, lost-response remount recovery despite a newer
draft, account changes before/after POST and during label/member/result reads,
foreign-scope response withholding, cancellation, owned keyboard focus and a
competing retained original. Thirty parser/transport/global-search tests also
pass. These are local source checks; they do not establish actual PostgreSQL
browser delivery or immutable native acceptance. Web/browser typecheck and lint
pass. Estimated PRD-16 work remaining is 18%; full native recovery, reconnect,
two-client and release acceptance remain required before closure.
