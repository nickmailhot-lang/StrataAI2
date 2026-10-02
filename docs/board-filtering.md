# Board filtering (PRD-10 / PRD-16)

Authenticated viewers of an active Board can read
`GET /boards/{boardId}/cards?keyword=...&labels=uuid,uuid&match=all&after=uuid`.
This initial server read supports keyword and label predicates. Member,
completion, due-date, recent-activity filters, anonymous PUBLIC Board parity,
and global search remain required PRD-16 work.

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
