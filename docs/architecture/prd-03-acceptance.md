# Organization acceptance map — PRD-03

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) remains open.
This source review maps the ten functional requirements to implemented behavior
and remaining work. It does not certify current release acceptance.

## Current departure execution

All six existing desktop/phone departure scenarios pass in one 3.2-minute
invocation against the current compiled Production API and web bundle through
current Nginx/CSP with real restricted schema-110 PostgreSQL roles. Actual cookie
replacement before/after submission, sole-owner continuity, Cancel/success focus,
lost acknowledgment, rejoin plus original-key recovery and pre/post-submission
profile uncertainty pass with unchanged assertions. The explicitly unverified
test policy matches the CI browser phase; strict verified-email ownership and
current retained-release acceptance are separate requirements. See
[executed departure evidence](organization-departure.md#executed-desktop-and-phone-departure-recovery).
Estimated PRD-03 work remaining is now **9%**; previous estimates below are
historical. The ticket stays open.

## Functional requirements

| Requirement | Current implementation and verification path | Remaining work or evidence |
| --- | --- | --- |
| WS-FR-001 authenticated creation | Organization service, owning transaction and [creation acknowledgments](organization-creation-retries.md); [Organization discovery](organization-discovery.md), native Organization command fixture and browser directory scenarios | Current production-policy, persisted creator/owner, retry and browser evidence |
| WS-FR-002 complete metadata | Organization record includes name, description, logo URL, owner, status and timestamps; [settings](organization-settings.md) | Current persisted-field and lifecycle verification; logo URL metadata is distinct from binary object storage |
| WS-FR-003 admin metadata editing | Server membership/version checks, settings draft reconciliation and [transactions](organization-command-transactions.md); desktop/phone settings scenarios | Organization-level change delivery and current durable metadata receipt evidence; current refusal, concurrency and browser evidence |
| WS-FR-004 email invitations | [Invitation administration](invitation-administration-ui.md), [creation retries](invitation-creation-retries.md), [recipient discovery](invitation-discovery.md), [delivery](../invitation-email-handler.md) | Current delivery/history/acceptance checks and full invitation lifecycle acceptance |
| WS-FR-005 safeguarded removal | [Member administration](organization-member-administration.md), [removal consent](organization-member-removal-consent.md), usable-owner checks and atomic assignment cleanup | Current same-key removal recovery, mid-session withdrawal/receipt rollback and native browser evidence |
| WS-FR-006 departure with continuity | Organization service departure under parent/membership locks; [confirmed browser departure and durable API receipt](organization-departure.md), owner-floor and concurrent-departure fixtures | Current final-session, account-switch, same-key recovery after rejoin, continuity and keyboard/mobile release evidence |
| WS-FR-007 authorized Board directory | Bounded active Board paging and direct reads in [discovery](organization-discovery.md), [access integrity](organization-access-integrity.md), [Board realtime](../organization-board-realtime.md) | Current restricted visibility, paging, access withdrawal and reconnect evidence; verify archived discovery alongside the active directory |
| WS-FR-008 OWNER/ADMIN/MEMBER roles | Canonical role enum, permission checks, directory and owner-floor rules | Current role-by-operation denial and usable-owner evidence across invitation/removal/departure |
| WS-FR-009 owner-only confirmed deletion | Server owner/version check transitions Organization to deleting and suspends reminders atomically; [durable request acknowledgments](organization-deletion-retries.md) | Current browser Owner confirmation/retry and native permission/keyboard/mobile evidence; terminal completion remains required, and a request is not completed deletion |
| WS-FR-010 graph/audit treatment on deletion | Deleting status withdraws normal access; request atomically suspends reminders and audits ORGANIZATION_DELETION_REQUESTED. Restricted terminal graph/lease gate passed real PostgreSQL CI at d715ce5; durable completion readiness passed restricted PostgreSQL CI at 736b386. Bounded reference traversal passed its 100,000-archived-Card contract at e01fa5a. Atomic Worker graph stages, selected cover/background cleanup and independent terminal observation passed restricted PostgreSQL CI; product atomic publication/terminal acknowledgment recovery is implemented with runtime validation pending | Complete the [lifecycle flow](organization-deletion-lifecycle.md): bounded Board/List/Card/attachment tombstones, retained attribution/provider evidence, product integration and Owner/two-client recovery. Staged terminal fixtures do not prove graph traversal or live consumption |

## Acceptance criteria and required scenarios

Current local member recovery evidence is documented in
[native terminal recovery](organization-deletion-lifecycle.md#native-internal-member-terminal-recovery).
Four real Production desktop/mobile connected/disconnected cases pass actual
separate Worker completion, exact canonical event recovery, cached-content
withdrawal, same-key Owner retry, private request refusal and logout withdrawal.
All four graphs retain their actual Board/List/Card tombstones and one ready
terminal event. This strengthens AC-WS-03-03 evidence; retained-image CI,
provider/backup treatment and remaining full acceptance are still required.

- **AC-WS-03-01:** actual registration/session policy, authorized Organization
  creation, persisted owner membership, browser acknowledgment and retry recovery.
- **AC-WS-03-02:** unauthorized metadata attempts return the stable protected
  refusal without changing metadata, membership, audit or related work state.
- **AC-WS-03-03:** completed deletion effects reach or recover in other authorized
  clients without manual full reload. Board directory events alone do not prove
  Organization deletion or metadata event delivery.
- **TC-01 through TC-13:** retain happy/empty/invalid/unauthorized, mid-session
  withdrawal, timeout/retry, duplicate/concurrent, reconnect, parent lifecycle,
  keyboard, mobile and applicable scale evidence against the relevant revision.
- **Definition of done:** verify all functional rows, server authorization,
  reviewed migrations, required domain events, executed unit/integration/browser
  checks, accessibility, loading/error states, telemetry and defect severity.

## Remaining acceptance order

1. Verify the implemented durable receipts, parent-before-membership locks and
   final actor admission in the current restricted persistence and image gates.
2. Inspect current retained-image browser departure, Owner deletion and
   metadata/lifecycle recovery outcomes alongside the scoped local executions;
   preserve refusal, account-switch, rejoin and original-key assertions.
3. Audit implemented bounded Worker graph/audit/event treatment against all
   lifecycle requirements, including outstanding private object/retention and
   scale evidence; request acceptance alone never proves completed deletion.
4. Review every functional row and required scenario against the full exact-commit
   pipeline and retained artifacts before closure. These implemented producer
   contracts do not need to be reinvented because older evidence remains pending.

Ownership transfer is not an explicit WS-FR requirement. Do not invent it as a
closure prerequisite; the required safeguards concern usable ownership during
removal, departure and deletion. Related PRD-05, PRD-18 and security contracts
still apply to implemented permissions and lifecycle behavior.

## Evidence boundary

The [Organization metadata event source](organization-metadata-events.md)
projects creation and editing audits into a private, forced-RLS, immutable
journal in the owning command transaction. Restricted PostgreSQL CI passed the
metadata source contract at `010324c` and strengthened assertions at `bf20c67`.
Exact-image rollback/retry fixtures still await runtime results. Migration 095
adds atomic reference jobs and leased Worker readiness with 17 local handler
tests passed; real database/upgrade/late-fence checks passed at `ab9a389`.
Migration 096 adds bounded automatic metadata routing and a typed invoker queue
claim with source/job fences and provider isolation. The full solution builds
cleanly; restricted database automatic routing checks passed at `6c43658`,
including 109 real source Organizations and provider isolation. Exact-image
automatic delivery checks remain pending CI. Authorized realtime/reconnect
consumption remains unfinished.

The [metadata replay endpoint](organization-metadata-replay.md) adds an owning
read transaction, protected actor/membership cursor, contiguous ready-prefix
window and final session/scope proof. Nine ordering/coordinator checks and the
cursor security test passed locally; restricted PostgreSQL replay passed at
`fe3376f`, including pending-prefix ordering, bounded continuation, final
synthetic session refusal and membership withdrawal/restoration. Exact-image
HTTP replay passed the exact-image metadata step at `fe3376f`; the overall
container run failed later on an unrelated mixed-job replay fixture, whose
type-scoping repair still awaits execution. Production SignalR transport is implemented with
session and cursor authority rechecks before each delivered page; stream
execution against release images remains required. Discovery and settings now
consume metadata changes, with preserved settings drafts and original-save
recovery; current native two-client acceptance remains pending.
Worker readiness does not establish browser event consumption.

Source review confirms that UpdateAsync, RemoveMemberAsync, LeaveAsync and
MarkDeletingAsync enter the Organization unit of work. MarkDeletingCoreAsync
marks status, reschedules reminders, audits and publishes the accepted request
with the first Worker job in the owning transaction. The separate Worker performs
terminal graph deletion. The Owner status reader checks independent completion
evidence; browser status consumption and tab-local account-bound recovery are
implemented with 1,565 full-suite checks and 36 final focused checks passed
locally. Native Worker/browser completion and end-to-end acceptance remain
unfinished. Current metadata, member-removal, departure and creation API contracts include
durable receipts. Browser creation recovery is implemented with current native evidence pending.
Deletion request acknowledgments and explicit browser Owner confirmation are
implemented with native evidence pending; terminal deletion remains incomplete; see their workflow guides for source and CI boundaries.

Historical native results apply to their recorded revision only. Queued CI and
successful compilation cannot close these gaps. Estimated work remaining is
**16%**, a planning estimate rather than a count of unchecked rows. Automatic
deletion discovery passed restricted PostgreSQL CI at `bfa46b4`; current native
terminal/two-client recovery, Demo terminal processing and applicable mutation
performance evidence remain required.

Internal Organization invitation acceptance now appends a distinct
`ORGANIZATION_MEMBER_ADDED` audit using the actual persisted membership ID and
accepting actor when it activates a new or inactive membership. Completed
invitation retries, existing active membership and Board/Portal acceptance do
not duplicate that addition. Acceptance and both audits share the existing
owning transaction. The required exact-image invitation fixture includes a
second-publication failure rollback check and canonical attribution assertions;
execution remains pending. Migration 097 now adds actual member-addition source
projection with private activation proof, independent membership revision and
canonical audit attribution. The existing typed Worker and protected replay
support it, and strict discovery/settings consumers accept its content-free
envelope. Local build, 12 replay tests and 33 consumer tests passed. Required
exact-image execution remains pending. Later restricted PostgreSQL runs verified
member addition at `de3331b`, removal/departure at `ea769e2` and invitation birth
sources at `8a09f28`, including ordered migration/upgrade checks and canonical
delivery/replay contracts. These are narrower than normal native commands and
do not certify all browser or real cookie/session races.

`1f03cc2` adds current Internal Member terminal replay and passed its restricted
PostgreSQL unready/ready source contracts. `7744663` adds the separate lifecycle
SignalR stream and Organization home consumption; local build, 59 client cases
and four common origin-guard cases passed. Subsequent recovery work starts the
separately authorized stream after account confirmation, so a new deep link can
recover completion even when ordinary graph admission is already 404. It also
checks late Board acknowledgments cannot navigate after deletion starts. The
native release fixture covers Member disconnect while the actual Worker finishes,
original source comparison, fresh-document recovery, both viewport widths and
actual logout. Those native additions still require execution against exact release
images. Full invitation/view lifecycle coverage, actual session races, Demo
audit/event/terminal parity and applicable mutation scale remain required.
Estimated remaining work stays **16%** pending runtime evidence. This ticket
remains open.

## Demo Portal recipient completion and retired-actor authority

Two additional API-host cases verify terminal recipient authority after real
HTTP account deactivation: accepted work retains the original actor, advances
the otherwise-current nonmember recipient cursor only at terminal publication,
and recovers an empty reset/quiet page without granting membership. Late failure
after actual completion readiness restores the pending parent and cursor before
retry. Private Owner observation and ordinary graph reads remain unavailable to
the Portal recipient; stale acceptance is refused and account access remains.

Four actual Demo desktop/phone connected/disconnected recipient browser cases
pass with the Owner's authoritative COMPLETED version/event/time and original
request retry. All pre-existing private-wire, cached-label withdrawal, held-read,
focus, no-reload, accessibility and overflow assertions remain. Production keeps
its original separate-Worker completion path. CI now requires these cases along
with the existing ten Demo metadata/member/Owner cases: fourteen retained-image
scenarios. See [recipient terminal authority](invitation-recipient-authority.md#demo-terminal-recipient-authority-after-actor-retirement).

Local compiled-runtime evidence does not establish current retained-image/full
release acceptance. Estimated PRD-03 work remaining stays **10%**.

The mandatory restricted persistence executable now also contains
`OrganizationDeletionScaleContract`, performing actual bounded mutation and
all durable event delivery for 5,000 active plus 100,000 archived Cards and 200
Lists. It uses the owning accepted publisher and normal Worker claim/handlers,
without admin checkpoint or lease staging, and checks retained attribution,
exact effects and restricted original-source recovery. This closes a missing
verification path. Its full 105,000-Card execution passed the mandatory
PostgreSQL job at `4f427d0` in
[run 37549390677](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37549390677):
826 bounded mutation jobs, 105,201 ready work events, one ready original terminal
source, 739,039 ms elapsed and 113 ms maximum leased mutation page. This proves
the restricted persistence workload; HTTP p95, deployed Worker/browser behavior
and provider/backup purge have separate acceptance requirements. Full exact-image
CI finished with a failed browser gate: 184 passed and 43 failed scenarios.
Source/persistence success does not establish a tested release. Subsequent
repairs address fresh terminal recovery, navigation landmarks, invitation expiry
scan deadlocks and Board history bootstrap; current exact-image verification
remains pending. Estimated remaining work stays **16%** pending the
remaining functional and full release/native acceptance evidence.

## Current creation and metadata verification

The local live settings and canonical metadata stream fixtures passed all four
complete desktop/phone cases on 2026-10-07, including unsaved-draft preservation,
original receipt recovery after a later edit, unchanged original event identity
through reconnect, logout revocation and accessibility. Separate Workers owned
only the newly created fixture Organizations and were retired afterward.

Six real reviewed-account settings cases then exposed a creation timestamp
precision mismatch. PostgreSQL creation now returns its actual stored row within
the original Organization/Owner transaction. A mandatory restricted persistence
contract fails before repair and passes three precision cases after it; full
record equality and initial Owner membership are asserted. Both isolated Release
builds pass with zero warnings/errors. All six desktop/phone actor cases pass
against the repaired API, keeping the original whole-record unchanged-state,
actual administrator replacement, command-count, disclosure, keyboard and axe
assertions. No native assertion or server admission was relaxed. See
[creation evidence](organization-creation-retries.md#canonical-persisted-creation-acknowledgment)
and [settings evidence](organization-settings.md#executed-local-metadata-and-reviewed-account-evidence).

Full release run 37622958815 at ed9bdc41 passed source, PostgreSQL, images and
security, then failed the Organization command fixture's exact snapshot equality
after member rejoin and receipt replay (line 612). That is a separate unresolved
failure. The fixture now reports only the names of differing snapshot sections
on refusal; the same strict equality and failure remain enforced. This diagnostic
withholds values and does not certify or repair the replay difference. Bash
syntax with Linux line endings passes; current exact-image execution is required.

Estimated PRD-03 work remaining is now **12%**, a planning estimate. Current
release acceptance, the replay snapshot failure, Demo terminal parity and remaining
lifecycle/performance requirements keep the ticket open.

## Replay snapshots and independent Worker progress

A disposable real-HTTP probe on 2026-10-07 created fresh accounts, invited and
accepted Organization and Board membership, assigned a Card, removed the member,
restored membership through a new invitation, and reassigned the Card. Fixture
email verification was seeded only for those accounts; this is not mail-delivery
evidence. The original removal key returned 204 and preserved the complete
post-rejoin snapshot with no dispatcher running.

The actual recipient authority dispatcher components then executed through the
restricted Worker PostgreSQL login, scoped only to this new Organization. They
changed only `background_jobs` fields `attempt_count`, `state`, `updated_at` and
`version`. Every membership, removal history, metadata event/stream, audit, Card,
assignment, immutable Work event/stream, removal receipt and immutable job field
stayed identical. Replaying the original removal key after dispatch again
preserved the complete snapshot. The isolated Release probe build passed with
zero warnings/errors; the probe exited successfully. This reproduces a race in
the whole-job snapshot, without proving the unidentified differing fields in
the older failed CI run.

The container job now starts all four discovery loops disabled. The Organization
command fixture verifies these flags and an empty scope in the running Worker
before creating any fixture or changing grants. All exact snapshot comparisons
remain intact. Its explicit deletion completion phase, automatic metadata phase,
and recipient/issuer authority delivery and restart phases still enable the
required real Worker loops. Production Compose defaults remain enabled; images
are still built once and shared by the unchanged mandatory release gates.

Before the complete authenticated browser suite, CI explicitly enables and
checks both automatic invitation authority loops in the running Worker. These
flags persist into the browser process so scoped Worker helpers restore the
same live routing. Nonmember and Portal issuer invalidation scenarios therefore
retain automatic delivery coverage.

The complete Organization command fixture then passed locally against the
current readonly compiled API/Worker and a separate PostgreSQL 17/pgvector
schema-110 database. This includes audit/receipt rollback, post-wait admission,
session-expiry rollback, concurrent owner departures, metadata/departure/removal/
creation/deletion retries, rejoin preservation, actual Worker graph completion,
completion-event delivery, original Owner status and exact terminal replay.
Five negative running-Worker cases also passed: each enabled discovery flag
and an explicit Organization scope were rejected before account creation or
grant cleanup, with account/receipt counts and protected grants unchanged.
No source assertion changed during these runs. The temporary runner first
lacked Python and then incorrectly hard-coded its deletion flag; those local
fixture errors were identified and corrected before the complete passing run.
The compiled Worker build, shell syntax and workflow YAML checks passed. These
are scoped local results, not evidence for the exact release-image gate.
Both automatic authority flags were also enabled and checked in the actual
compiled Worker, then restored to the isolated base; the browser-routing shell
block passed syntax validation. This verifies phase configuration, not the
complete native invitation acceptance scenarios against current release images.

Current exact-image execution is pending. This correction establishes fixture
isolation, not full release acceptance or complete PRD-03 delivery. Estimated
remaining work stays **12%** pending that execution and the other requirements.

## Demo metadata source and browser recovery

Demo now publishes all seven nonterminal canonical Organization metadata source
types through the owning command: creation/editing, member addition/removal or
departure, and Internal invitation creation/acceptance/revocation. The journal
retains original audit identities and actual subject revisions/timestamps,
requires same-command transition proofs, excludes Board and Portal invitation
surfaces, and rolls back sources and sequence counters on late refusal. Protected
HTTP and SignalR replay share the existing session/membership fences, cursor
binding, bounded pagination and authoritative reset contract. Browser consumers
now open this admitted channel in both runtime modes.

Seven new API cases and all 91 selected Demo API cases passed on 2026-10-07.
The new tests first failed on the unmapped endpoint; a later wire-test helper
needed explicit DTOs for the existing multi-constructor event contract. No
application serialization or acceptance assertion was weakened. Coverage includes
original-key recovery, canonical source attribution, pagination, wrong actor and
Organization refusal, membership withdrawal/restoration, Portal/Board exclusion,
and rollback after failure, actor refusal, exception or cancellation. A waiting
read also observes only restored committed history after rollback.

All 250 related web cases and the complete 134-file, 1,896-case web suite passed
with two workers. Web and browser TypeScript checks, zero-warning lint, production
web build, workflow YAML, Bash syntax and retained-image Compose configuration
validation passed. Four actual desktop/phone Demo browser cases passed without
changing assertions: source-ID replay and reconnect, server logout withdrawal,
peer saved versions, preserved drafts, lost-acknowledgment original-key/body
recovery, keyboard operation and automated accessibility. They ran against a
fresh readonly compiled Demo API and Vite, with explicit Demo mode and CI enabled.

CI now requires the same four scenarios on a disposable Demo stack using the
retained API/web release images. It adds no rebuild or provider and retains
failure logs. Production still uses its separate durable Worker and existing
PostgreSQL/RLS source; Demo publication is synchronous in-memory simulation.
API restart resets the Demo journal, while sample-catalog reset leaves it intact.
Exact-image results for this revision are still pending; these local checks
do not establish complete release acceptance or terminal deletion parity.

Estimated PRD-03 work remaining is **11%**, a planning estimate. Demo terminal/
lifecycle processing, remaining lifecycle/performance requirements and complete
current release acceptance keep the ticket open.

## Demo graph page implementation

The [Demo bounded graph simulation](organization-deletion-lifecycle.md#demo-bounded-graph-simulation)
now handles attachments, archived/active Cards and Lists, Board tombstones and
retained cover/image references under the owning accepted request. Six local
API-host checks pass, including actual event/audit rollback, a 130-archived-Card
page boundary, file-integrity retention, unchanged other-tenant records and
current original-Owner/request/version/limit admission. The API host builds with
zero warnings/errors.

This supplied the graph prerequisite for WS-FR-010. Automatic dispatch and
protected completion recovery are now connected as recorded below. The bounded
fixture alone does not prove supported-scale performance or full release
acceptance; the estimate at that prerequisite revision was **11%** remaining.

The fresh full solution builds with zero warnings/errors. All 739 domain checks
pass against the same compiled output in a disposable Linux SDK container with
readonly source/output mounts and no external network. The native Windows domain
run has 16 failures (723 passes), including socket binding, symlink privileges
and timezone behavior; it is not reported as a passing run. The architecture
dependency check initially failed to find source from the external artifact
directory. Its lookup now considers the actual compile-time source and working
directory as well as output ancestors, and the unchanged project-reference rules
pass in the fresh external build. Missing source still fails the check.

## Automatic Demo terminal lifecycle and native recovery

The Demo API now automatically discovers immutable accepted requests and executes
the actual bounded graph pages. It commits terminal parent/version/attribution,
actual audit, recipient authority effects and one canonical completion source
together, with full rollback on late failure or cancellation. The committed
request supplies execution authority independently of the browser session.
Protected original-Owner observation and current-member HTTP/SignalR lifecycle
recovery enforce current account/session/membership admission. Production retains
the separate durable Worker and restricted PostgreSQL jobs; Demo processing is
process-local and has no restart durability or provider-erasure claim.

All five new API-host cases and all 54 selected PRD-03 cases pass. The latest
fresh solution build has zero warnings/errors; all three known Demo-account
checks also pass against that build. A fresh readonly compiled Demo API in
Development and production web bundle pass six actual browser cases: desktop
and phone original-key recovery after deliberately lost acknowledgment, exact
terminal event/version/time, connected and genuinely disconnected member
recovery without document reload, cached-content withdrawal, private Owner
request refusal, fresh terminal deep links, account replacement/logout,
keyboard focus and accessibility. No SQL or test code fabricates terminal state.

All four Demo metadata/settings browser cases also pass against the updated
runtime and bundle, for ten passing Demo native scenarios in total. Browser
TypeScript, zero-warning lint, production web build, workflow YAML, all 141 Bash
step blocks and retained-image Demo Compose configuration checks pass.
CI now requires those ten scenarios using only the retained API/web images. Local framework
containers and production bundles do not prove the retained-image gate. The
current full web source run has reported failures and remains under review;
focused lifecycle checks passing is not reported as full web acceptance.

Estimated PRD-03 work remaining is **10%**, a planning estimate. Current complete
release acceptance, remaining lifecycle/performance coverage and applicable
retention treatment keep this ticket open.

The exact `6bee3ce5` Linux CI source gate has since passed: all **739 domain**,
**600 API-host** and **1,897 web** cases, plus restricted PostgreSQL integration.
The separate native Windows web invocation finished with 1,894 passes and three
activity/comment failures; the two unchanged affected files then passed all 46
cases in a scoped repeat. This is not a clean full Windows invocation or a
claimed root-cause repair. Retained-image container acceptance for that revision
failed at the Demo authentication smoke step with exit 22; security and image
build passed, required-ci failed and the release bundle was skipped. This is a
failed release gate despite the complete source pass.

## Demo legacy discovery withdrawal and isolated workflows

The Demo smoke failure also reproduces locally with the API's network disabled:
after successful collaboration and password reset, Owner-continuity setup tries
to invite into a deleted Organization still returned by legacy discovery. The
invitation endpoint correctly refuses it with 404. Demo's `GET /organizations`
now excludes DELETING and DELETED parents just as PostgreSQL already does.
Historical membership, audit data and protected independent completion recovery
remain intact. A real API-host regression first fails on pending-parent exposure,
then verifies both pending/completed Owner and member discovery withdrawal,
unchanged active-Organization access and completed private recovery.

The fresh full solution build has zero warnings/errors. All **55 selected
PRD-03 API-host cases** pass. The unchanged complete Demo authentication/workflow
smoke suite now passes against the fixed compiled API with Docker **network=none**,
no published port, no PostgreSQL and no production provider credentials. The
namespace-local client verifies actual seeded login, CSRF, profile concurrency,
Organization lifecycle, Internal/Portal separation, Board/List/Card operations,
password reset/session revocation and Owner-continuity-safe deactivation.
This local runtime uses a readonly compiled output mounted in a cached API
runtime image; it is not retained-current-image acceptance.

CI now runs those same smoke assertions in the exact retained API's isolated
network namespace and includes four passing prerequisite/refusal/cleanup source
fixtures. The [runtime guide](runtime-modes.md#demo-isolation-verification) explains
the Linux test-client requirements and verification limits. Current new-image
results remain required. Estimated PRD-03 work remaining stays **10%**; the ticket
remains open.
