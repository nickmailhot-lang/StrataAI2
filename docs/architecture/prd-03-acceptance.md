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
automatic delivery checks remain pending CI. Authorized realtime/reconnect consumption remains unfinished;
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
**18%**, a planning estimate rather than a count of unchecked rows. Automatic
deletion discovery passed restricted PostgreSQL CI at `bfa46b4`; current native
terminal/two-client recovery, Demo terminal processing and applicable mutation
performance evidence remain required.
