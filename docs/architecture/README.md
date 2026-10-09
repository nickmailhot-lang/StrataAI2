# StrataAI2 architecture

The GitHub architecture PRDs (ARCH-01 through ARCH-12) are authoritative.

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

- [Recipient authority revision clocks and private publication](invitation-authority-revision-clocks.md)

[Card route statement projection](card-route-statement-projection.md) documents batched canonical
route synchronization, preserved security/clock boundaries and scale verification.

[Owner Portal screen verification](portal-screen-verification.md) records four-width admission,
reflow, unavailable browsing state and the remaining release/feature boundaries.

[Public error correlation references](public-error-references.md) explains safe support
identifiers, preserved error privacy and the remaining error-display review.

[Organization settings error references](organization-settings-error-references.md) records response-bound
support identifiers, account withdrawal and original-save recovery boundaries.
