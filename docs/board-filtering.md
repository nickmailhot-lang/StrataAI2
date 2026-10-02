# Board filtering (PRD-10 / PRD-16)

Authenticated viewers of an active Board can read
`GET /boards/{boardId}/cards?keyword=...&labels=uuid,uuid&match=all&after=uuid`.
This initial server read supports keyword and label predicates. Member,
completion, due-date, recent-activity filters, anonymous PUBLIC Board parity,
session-persisted Board controls and global search remain required PRD-16 work.

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
Strict local compilation and fixture syntax checks passed. Host and PostgreSQL
runtime assertions await Linux CI; local Windows execution is restricted.

Neither PRD-10 nor PRD-16 is complete. The remaining filters, UI, event/reconnect
acceptance and performance evidence must be implemented and verified before
closure.
