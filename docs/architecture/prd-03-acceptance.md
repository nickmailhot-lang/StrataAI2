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
| WS-FR-010 graph/audit treatment on deletion | Deleting status withdraws normal access; command suspends reminders across the full candidate Board set and appends ORGANIZATION_DELETION_REQUESTED | Define and implement end-to-end Board/List/Card/attachment retention or cleanup, retained attribution/audit treatment, terminal deletion and required ORGANIZATION_DELETED publication |

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

Source review confirms that UpdateAsync, RemoveMemberAsync, LeaveAsync and
MarkDeletingAsync enter the Organization unit of work. MarkDeletingCoreAsync
marks status, reschedules reminders and audits a deletion request; it does not
perform terminal graph deletion. Current metadata, member-removal, departure and creation API contracts include
durable receipts. Browser creation recovery is implemented with current native evidence pending.
Deletion request acknowledgments and explicit browser Owner confirmation are
implemented with native evidence pending; terminal deletion remains incomplete; see their workflow guides for source and CI boundaries.

Historical native results apply to their recorded revision only. Queued CI and
successful compilation cannot close these gaps. Estimated work remaining is
**24%**, a planning estimate rather than a count of unchecked rows.
