# Organization acceptance map — PRD-03

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) remains open.
This source review maps the ten functional requirements to implemented behavior
and remaining work. It does not certify current release acceptance.

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

## Next implementation order

1. Complete retry-prone Organization mutations with durable receipts while
   preserving parent-before-membership locks and final actor admission.
2. Add browser departure and owner deletion confirmation with explicit recovery
   and unchanged-state refusal checks.
3. Implement and document deletion graph/audit treatment and required terminal
   events with the separate Worker and private object-storage contracts.
4. Verify Organization metadata/lifecycle delivery and reconnect behavior in two
   clients, then inspect the full exact-image release checks before closure.

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
