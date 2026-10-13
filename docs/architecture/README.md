# StrataAI2 architecture

[Job-column and URL draft failure evidence](failure-field-diagnostics.md) preserves full comparisons and documents the six original scenarios plus controlled diagnostic failures.

[Organization review and removal recovery verification](organization-review-recovery-verification.md) records three reproduced recovery-state defects and complete desktop/phone configuration and membership verification.

[Checklist reads and file chooser opening verification](checklist-file-opening-verification.md) records retained hosted opening failures, the retired-launcher helper correction and all four final desktop/phone cases.

[Retention source opening verification](retention-source-opening-verification.md) records the existing Comment disclosure failure and all 14 final classification/hold browser cases.

[Movement and peer activity admission](movement-activity-admission-verification.md) records the busy-Board drag trace and complete desktop/mobile movement and strict activity checks.

[Read-only checklist and invitation opening verification](readonly-directory-opening-verification.md) records retained hosted failures, the mobile navigation correction and complete final desktop/mobile cases.
[Invitation history head admission](invitation-history-head-admission-verification.md) records immediate authority withdrawal, the Cancel-focus regression and complete desktop/phone verification.
[Board member consent after live admission](member-history-head-admission-verification.md) records the retained timeout, reviewed-dialog regression and complete strict browser verification.

[Permission-order component diagnostics](watch-order-component-diagnostics.md) preserves complete state comparisons and records a deliberate failure probe for safe hosted diagnosis.

[Archived Card recovery admission](archived-card-recovery-admission-verification.md) records observed original receipt recovery and complete desktop/mobile verification supporting the operational MVP.

[Board starring review opening](board-star-opening-verification.md) records the hosted admission interruption and complete local desktop/mobile verification for the operational MVP.

[Label keyboard admission verification](label-keyboard-admission-verification.md) records the hosted consent interruption and local desktop/mobile checks supporting the operational MVP.

The GitHub architecture PRDs (ARCH-01 through ARCH-12) are authoritative.

[Checklist review admission](checklist-review-admission-verification.md) records
the failed nested opening, complete desktop/mobile repair proof and preserved
conflict, dirty-draft, socket, original-receipt and revocation requirements.

[Watch-order state investigation](watch-order-state-investigation.md) records
complete current four-role local execution while preserving the unresolved
hosted whole-state hash failure and every original assertion.

[Invitation canonical recovery](invitation-canonical-recovery-verification.md)
records automatic versus explicit read-only recovery, stale-error removal,
the exactly-one-DELETE proof and complete desktop/mobile verification.

[Attachment review opening verification](attachment-review-opening-verification.md)
records the paired archive/cover reproduction and ordinary browser proof while
preserving access guards, exact byte/write checks and simulated-provider limits.

[Creation form admission verification](board-creation-admission-verification.md)
records the retained tablet failure, controlled protected-read reproduction,
and complete desktop/tablet/mobile persistence and isolation verification.

[Activity history recovery verification](activity-history-recovery-verification.md)
records the retained CI trace, controlled before/after reproduction and complete
ordinary desktop/mobile verification while preserving protected-page purging.

The [operational MVP milestone](operational-mvp-milestone.md) prioritizes core
workflow reliability and verified deployment before new governance and AI work.
The [API contract acceptance map](prd-21-acceptance.md) and
[error-envelope verification](api-error-contract-verification.md) distinguish
implemented contracts from the remaining complete release evidence.

[Current invitation, comment and cover verification](invitation-comment-cover-verification.md)
records the complete native browser invocation, confirmation focus regressions,
and the distinction between actual persistence and simulated cover providers.

The [PRD-01 foundation acceptance map](prd-01-acceptance.md) links the complete
hierarchy/navigation requirements, acceptance criteria, test scenarios and
remaining audit/release scope to their verification sources.

[Notification journal history](notification-event-history.md) records retained
source validation, append-only enforcement and the complete migration/security
proof, with native and immutable release acceptance tracked separately.

[Work receipt update clocks](work-replay-update-clocks.md) records future
receipt completion/update timestamps while preserving unknown legacy times,
with migration, restricted-role and readiness verification boundaries.

[Attachment sweep checkpoint clocks](attachment-sweep-audit-clocks.md) records
prospective preview/recovery audit times, original Worker capability boundaries
and exact legacy cursor preservation with unknown historical clocks.

The [invitation issuer clock audit](invitation-issuer-clock-audit.md) identifies
owning writers, immutable facts and the remaining mutable-job clock gap.

The [Board background ownership clock audit](board-background-clock-audit.md)
traces immutable preview ownership, canonical selection clocks and installed
schema-116 metadata evidence.

[Canonical entity route clocks](entity-route-clocks.md) records migration 117's
source-derived projection clocks and the remaining verification scope.

The implemented web routing and query-state choices, their executable evidence
and outstanding ARCH-02 audit scope are recorded in
[Web SPA routing and state boundary](web-spa-boundary.md).

## Dependency direction

```text
StrataAI.Api ---------+
                      |
                      v
              StrataAI.Application
                      |
                      v
                StrataAI.Domain

StrataAI.Worker ------+

StrataAI.Infrastructure implements interfaces consumed by Application and may depend on
Application/Domain. Domain must not depend on ASP.NET Core, PostgreSQL, provider SDKs,
Docker, AI providers, email providers, or object storage implementations.
```

## Runtime processes

- **API** — HTTP, authentication/authorization, commands/queries, realtime endpoints.
- **Worker** — durable/background work such as email intake, AI processing, indexing,
  notifications and recurring tasks.

Both are part of one modular-monolith product and share the same domain/application code.

- [Work event delivery clocks and legacy provenance](work-event-clock-audit.md)

- [Invitation mail update clocks and delivery boundaries](invitation-mail-update-clocks.md)
- [Invitation support references and verified recovery evidence](invitation-error-references.md)

- [Recipient authority revision clocks and private publication](invitation-authority-revision-clocks.md)

[Card route statement projection](card-route-statement-projection.md) documents batched canonical
route synchronization, preserved security/clock boundaries and scale verification.

[Owner Portal screen verification](portal-screen-verification.md) records four-width admission,
reflow, unavailable browsing state and the remaining release/feature boundaries.

[Public error correlation references](public-error-references.md) explains safe support
identifiers, preserved error privacy and the remaining error-display review.

[Organization settings error references](organization-settings-error-references.md) records response-bound
support identifiers, account withdrawal and original-save recovery boundaries.

[Authentication and recovery support references](identity-error-references.md) covers
response-bound references, safe retry/cancellation and executed source checks.

[Profile and account error references](profile-error-references.md) covers
protected refresh, saves, sign-out, deactivation and retained verification boundaries.

[Primary-screen long-name reflow](primary-screen-reflow.md) records
four-width long-content verification, MUI wrapping and release-collection scope.

[ARCH-03 backend acceptance map](arch-03-acceptance.md) distinguishes
implemented core boundaries, missing module coverage and the Demo job-store gap.


[Label creation support references](../board-label-api.md#label-creation-public-failure-references) records actual response-bound
identifiers, unchanged rejection state, original retry identity and desktop/phone
error-dialog accessibility and reflow evidence.

[Label management support references](../board-label-api.md#label-management-public-failure-references)
records directory-read and real competing-edit refusal coverage, safe reference
retirement and the retained baseline and verification boundaries.

[Card label support references](../board-label-api.md#card-label-public-failure-references)
records disclosure/editing recovery and its source and native browser evidence.


[Records retention contracts](records-retention.md) define the initial PRD-70 policy, hold and disposition rules, their canonical entity design and the complete remaining runtime/acceptance work.
