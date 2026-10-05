# Organization Board archive realtime acceptance

PRD-04 requires other authorized clients to receive or recover relevant shared
Board changes. The archive directory currently uses foreground/poll recovery;
its Organization-wide client subscription and native acceptance are still
unfinished. The server now exposes the protected `/organizations/live` SignalR
transport described below; execution of its new transport tests is pending CI.

Migration 073 introduces an Organization-ordered projection of the six canonical
shared Board lifecycle/metadata events. The rows reference existing Work event
IDs; no new audit history, fake actor, copied metadata, star preference or
Card/Watch/Reminder activity is manufactured. A narrow parent-row trigger
projects actual source insertion in the same transaction, including rollback
and duplicate-source semantics. Its tenant counter serializes cross-Board
publication before commit. Historical backfill has a deterministic created-time
and event-ID order and does not rewrite source history. Migration installation
fences source writers through backfill/trigger creation.

Both tables have forced tenant RLS and tenant-affine foreign keys. Runtime API
access is SELECT only; the Worker keeps its existing narrow readiness capability
on the original event. The hardened trigger capability derives every effect
from the actual inserted parent and is not directly callable by runtime roles.
A journal row remains pending until its canonical source becomes ready. This
projection alone is not authorization to disclose a Board or its envelope.

The required PostgreSQL source test exercises restricted insertion, two Board
streams sharing one Organization order, another tenant, original event IDs,
pending/readiness, a declared late owning rollback, duplicate insertion,
no direct runtime journal/counter writes, immutable history and private-star
exclusion. Populated migration upgrade/repeat coverage requires exact eligible
source counts, matching stream heads and unchanged source bytes. The existing
raw activity-source contract cleans its explicitly synthetic history through
an administrative transaction, including its dependent projection, and restores
the guard before commit; this does not expose a runtime retention path.

Strict local .NET build and migration-runner syntax checks pass. The new migration/storage checks passed PostgreSQL job 111598807028 in
[run 37257907574](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37257907574)
at `bf241b5`, including populated upgrade/repeat and the restricted projection
assertions. The enclosing genuine C# persistence fixtures also passed, including
synthetic source cleanup and all three staged image-admission variants. The separate required concurrency fixture holds a first restricted runtime
transaction after its real Board source insertion, observes a second Board's
actual transaction-ID lock wait, then releases and verifies committed sequence
order despite reversed source clocks. Neither source may be visible before
release or acquire fabricated readiness. Its observation loop is bounded and
fails if the lock is not observed; it is not a browser retry/pacing change.
The concurrency case passed PostgreSQL job 111599823121 in
[run 37258258099](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37258258099)
at `ca227ec`. Its inspected success marker follows the actual counter wait,
pre-release visibility check, both commits, exact ordered original event IDs,
head two and zero fabricated ready-source assertions. This is restricted-role
source/storage evidence, not an HTTP session/audience or live-browser result. Current actor/Organization/qualifying Board administration before and after IO,
privacy-bound cursor recovery, bounded delivered reads, the demo adapter,
SignalR session withdrawal and the MUI archive consumer still need implementation
and executed acceptance. No raw journal endpoint is exposed. Current foreground
polling must not be described as completed Organization live updates.

The next storage increment adds an unregistered PostgreSQL reader that locks the
active Organization and actor membership, derives Organization administration
from the locked role, and filters qualifying Board administrator tuples before
the bounded limit+1 read. Board and qualifying grant tuples are locked together
to recheck demotion after a lock wait. It retains canonical source readiness and
original event identity, emits no actor/correlation/body fields, and accepts gaps
from other Boards without skipping an eligible pending source. Internal numeric
positions are not a client cursor and must not be exposed. Unit cases cover
gaps, pending recovery, lookahead replay and malformed ordering; the mandatory
restricted C# persistence contract adds storage audience, source identity and
demotion checks. Their execution is pending CI. This reader is not registered as
a service or endpoint; permission epochs, protected cursors, post-IO session
admission, demo parity and the live UI remain outstanding.

Migration 074 introduces per-subject permission admission metadata, populated
from canonical Organization memberships. Actual administrative Board grant
expansion/withdrawal and Organization role/status changes revise only the
affected subject, in the same owning transaction. A fresh random generation
survives ordinary removal/restoration and changes on hard membership replacement;
the foreign key cascades explicit parent cleanup. No-op writes and unrelated
nonadministrative Board memberships do not revise it. Runtime access is SELECT
only; narrow parent-row trigger capabilities cannot be directly called by the
API. The revision is not a Board event or an audit substitute.

The registered cursor codec encrypts internal bigint positions using a separate
Data Protection purpose. Binding includes actor, Organization, actual membership
identity, parent version, permission generation and revision, with a 15-minute
expiry. A changed grant/rejoin/parent binding rejects the old token without
returning its numeric position. This does not yet wire the codec into a live
endpoint or implement permission reset behavior. Required SQL tests cover tenant
isolation, actor-only revisions, rollback of revision and clock, withdrawal and
restoration, no-op/direct-write/capability refusal and replacement generations;
cursor tests cover binding dimensions, exact bigint preservation, corruption,
purpose separation and expiry. Migration upgrade/repeat checks require exact
backfill counts and unchanged canonical membership/source bytes. Execution of
these new checks remains pending CI.

The first reader CI at `13631fd` failed its initial composite storage-audience
assertion in PostgreSQL job 111606784110. The fixture now separates active-member
admission, envelope audience and pending-state assertions, retaining all three
requirements to identify the cause. No runtime success is claimed for that
reader until the failure is resolved and its mandatory contract passes.

The separated assertion at `02d1680` identified legitimate earlier Board
envelopes, rather than failed Organization admission or unauthorized pending
state. Earlier mandatory fixtures retain sources on Boards this shared actor
administers. The reader fixture now captures the journal head immediately before
its own 65 source insertions and applies every audience/pending/identity/demotion
assertion to that exact source range. It retains the zero-envelope and no-pending
requirements before its own grant and after demotion; no production audience
filter is relaxed. The required permission-epoch SQL check passed in PostgreSQL
job 111608298905 at `02d1680`; the enclosing C# reader case remains pending repair
verification. This is metadata/storage proof, not live transport acceptance.

An unregistered Application coordinator now binds replay to the actual current
scope before and after awaited reads. Bootstrap or an invalidated protected
cursor yields a fresh snapshot boundary with no historical envelopes. Withdrawal
or a changed permission binding during the read rejects the whole response,
including empty pages and cursors. Event payloads contain original event/Board
identity and canonical type/version/time without numeric positions, actor,
correlation or body fields. PostgreSQL scope reads lock parent/member tuples but
do not lock the epoch before Board grant tuples, avoiding inversion against
grant writers. The mandatory C# fixture exercises real bound-cursor reset on
grant expansion and demotion; unit coverage exercises scope denial, empty-page
withdrawal, change-and-restoration during replay and original source identity.
These additions compile but await CI execution. The coordinator remains
unregistered until the owning read transaction, demo parity, authenticated
transport and UI are implemented and verified.

At `550308e`, PostgreSQL job 111609411638 reached the coordinator fixture but
could not construct the cursor codec because the minimal persistence provider
did not register `IClock`. The fixture now explicitly registers the application's
`SystemClock`, matching API startup. Codec expiry and every reader/coordinator
assertion remain unchanged; actual repaired execution is still pending CI.

The reader/coordinator cases reached their success marker in PostgreSQL job
111609976527 at `b4e0cd3`, including the actual bound-cursor reset checks before
that marker. The enclosing legacy private Card-history refusal then failed
because the temporary reader grant was still present after demotion: a Member
grant still authorizes normal private Board viewing. Commit `5a8de82` retires
that exact synthetic grant before the historical refusal starts, preserving
both sets of assertions. Full enclosing execution still needs verification.

Both runtime modes now register the coordinator behind an owning Work read
wrapper. It checks current parent/membership and delegates to the existing
transaction's initial/final actor proof. Demo projects only actual six canonical
Board sources into its own Organization order, preserving IDs and rolling back
the projection/head with its source. Demo's existing immediate delivery behavior
is retained; it is not evidence for PostgreSQL Worker readiness. Administrative
Board membership transitions revise an actor-specific counter, included in the
Work rollback snapshot; the actual Organization membership identity/version
covers its role/withdrawal/restoration. These values compose the demo permission
binding without content hashes or another user's grant activity. Demo metadata
version updates can conservatively reset a cursor even without a role change.
The registered coordinator is not yet exposed through a live endpoint.

A demo adapter test seeds explicitly synthetic canonical sources, proves 64
ineligible events do not displace an eligible event before the page cap, checks
50+15 owner paging/source identity, and declares a late refusal after both a
grant demotion and source projection to require complete rollback. It then
checks a real committed demotion invalidates the protected cursor. This is
storage/transaction coverage, not lifecycle/Worker/browser proof. These new
runtime registrations and tests compile; actual execution remains pending CI.

The complete enclosing PostgreSQL fixture passed job 111610757085 at `5a8de82`
and job 111611269739 at `7e2fe4a`, including directory permission epochs, actual
reader/protected-cursor checks, the subsequent private historical Card refusal,
and the final restricted persistence success marker. This verifies retirement
of the temporary grant without weakening either audience boundary. The new
demo adapter test still requires its own .NET execution; PostgreSQL success is
not demo, authenticated SignalR or native two-client evidence.

The full source .NET job 111611269561 passed at `7e2fe4a`; CI runs the complete
Domain/API assemblies without a test filter, including the new demo adapter and
transaction case. The PostgreSQL and web jobs also passed for that revision.
This is source/adapter execution, not a green immutable-image release.

The server's `/organizations/live` hub requires authentication and the existing
strict configured-origin middleware. Each connection has at most one stream,
with cancellation releasing its slot. The current cookie session and verified
email policy are checked before and after replay selection; the delivered
opaque cursor is then checked against current permission scope after session IO
before yield. Losing admission closes the stream without disclosing its page.
Pages are bounded by the coordinator and retain no actor/correlation/body fields
or raw positions. Bootstrap, changed permission bindings and expired cursors
require a fresh snapshot; normal resume uses canonical delivered sources.
The existing one-second server idle cadence and twenty-cycle heartbeat are used.
Nginx and Vite route the new WebSocket path with the adopted same-origin proxy
behavior; other deployment components and immutable-image gates are unchanged.

New WebSocket cases use actual HTTP Board archive/restore producers in demo,
check body-free event shape, disconnect/restore/replay, administrative demotion
reset, Organization membership withdrawal, logout, one-stream bounds and slot
release, plus missing/foreign-origin refusal. A coordinator test additionally
checks the final cursor proof after a permission change following an admitted
page. These additions compile with warnings as errors but await CI execution.
They do not prove PostgreSQL Worker readiness, proxy traversal or the MUI
two-client experience; those require the current exact-image acceptance fixture.

The browser connector now validates exact Organization/current-viewer scope,
opaque cursor shape, the six body-free source types, UUIDs, timestamps, safe
versions, bounded pages and reset semantics before retaining a cursor. The
private transport explicitly names only its current viewer, never the source
actor, so a changed-session packet cannot bind to an old browser identity.
Reused canonical event identity with different content is refused. Known
duplicates do not refresh the directory, and newly encrypted empty heartbeat
cursors do not masquerade as domain changes. Seen identity retention is capped
at 1,000; actual invalidation is coalesced across a short source burst.

The connector resumes the exact opaque cursor, fences old streams by generation,
and cancels subscription/recovery/refresh timers on disposal. Reset and transport
unavailability have distinct callbacks for the archive UI's disclosure and
pending-command handling. Its 14 focused validation/connector tests pass locally,
including foreign viewer refusal, malformed/private fields, corruption-shaped
cursors, duplicate identity, reset, heartbeat and stale subscription/cleanup.
ArchivedBoardsPage now subscribes after current profile and directory admission.
Canonical invalidation, reset and transport loss immediately withdraw cached
names and review consent and fence older reads before re-admission. Unconfirmed
commands retain their original key for an explicit private retry; confirmed
identity or scope loss retires them. The combined connector and archive suite
passes 29 local tests. The preceding transport revision 9c4eaeb passed CI
Domain/API host tests, web quality and PostgreSQL integration; exact-image
release acceptance and this browser increment are still pending.
Native two-client/proxy/Worker evidence is still outstanding.

The desktop and phone organization-board-live browser scenarios now collect real
upstream WebSocket frames, compare lifecycle IDs with the original Board replay,
exercise disconnect/resume without fabricated messages, and require cached names
and destructive review to disappear on logout. The lifecycle receipt scenario
now requires canonical deletion to remove stale consent while preserving the
original retry key. Browser fixture typecheck and discovery pass locally; these
new cases await exact-image execution and are not claimed as passed acceptance.

A further phone scenario uses a MEMBER with one explicit Board Admin grant.
It places a real ineligible Board lifecycle source before an eligible source,
requires only the admitted Board envelope and archive name, then removes the
Organization membership while destructive review is open. The session remains
valid but directory access must refuse and private review must disappear.
This fixture compiles; its actual PostgreSQL/proxy execution remains pending.

The phone audience scenario also changes only the explicit Board Admin role
to MEMBER on an archived Board and requires a genuine empty reset, removed
archive name and retired destructive review. A refused restore must retain the
original archived version. Regranting ADMIN must emit another reset and recover
the current archive snapshot without restoring old consent. Only then does the
scenario remove Organization membership. Compilation and collection are local
evidence; current exact-image execution is still required.

Migration 075 begins the ordinary-reader discovery extension. Separate reader
revision/clock fields track personal nonadmin grant admission and ordinary
Organization members' Board visibility transitions, while existing archive
revision/clock fields retain their original semantics. Content edits and no-op
visibility writes do not create private activity signals for ungranted readers.
The migration retains forced tenant RLS and SELECT-only runtime epoch access;
new trigger capabilities cannot be called directly. Upgrade evidence must prove
existing archive metadata and original Work sources unchanged. CI adds actual
reader grant/withdrawal, role/no-op, visibility, rollback and capability tests.
The warning-free build passes. New PostgreSQL execution is pending, and wider
reader cursors, audience replay, transport and active-directory UI are not yet
implemented; this foundation does not complete active Board directory realtime.

At 101e1be, run 37266353340 PostgreSQL job 111623913733 passed the complete
new migration/upgrade, ordinary-reader admission fixture, archive separation and
restricted C# persistence integration. The fixture now includes the existing
tenant-scoped routing write capability needed by real Board metadata updates;
production permissions are unchanged. This proves the reader epoch storage
foundation, not the still-required wider audience/cursor/transport/UI extension.
