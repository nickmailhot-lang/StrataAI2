# Documentation guide

Return to the [project README](../README.md).

Use this index to find every document in `docs/`. Architecture documents describe shared contracts and implementation decisions; feature documents describe specific behavior; acceptance and evidence documents distinguish verified behavior from remaining work. GitHub PRD/ARCH issues remain the authoritative requirements.

## Folder map

| Location | How to use it |
| --- | --- |
| [Architecture overview](architecture/README.md) and `architecture/` | Understand dependency direction, shared services, persistence, security, runtime configuration, and acceptance boundaries. |
| Documents in this folder | Look up feature behavior, implementation notes, and focused browser or release evidence. |
| [Release guide](release/README.md) and `release/` | Run the exact Docker images verified by CI. |

## Suggested starting points

- New contributor: [architecture overview](architecture/README.md), [runtime modes](architecture/runtime-modes.md), [configuration](architecture/configuration.md), and [API host testing](architecture/api-host-testing.md).
- Implementing a ticket: [dependency audit](ticket-dependencies.md), [linked dependency map](ticket-dependency-map.md), the feature document below, then its shared contracts and acceptance records.
- Reviewing completion: [foundation acceptance](architecture/prd-01-acceptance.md), [Board acceptance](architecture/prd-04-acceptance.md), [activity acceptance](architecture/prd-15-acceptance.md), and the relevant evidence documents. Match every result to its recorded revision and test scope.
- Operating the application: [release guide](release/README.md), [database roles](architecture/runtime-database-roles.md), [schema upgrades](architecture/schema-upgrades.md), and [operator metrics](architecture/operator-metrics.md).

## How to navigate

1. Choose a subject below, then open the behavior or contract guide that matches your question. Features can have guides both here and in `architecture/`.
2. Follow the guide's related links to permissions, persistence, retries, and acceptance evidence. On GitHub, use the document outline to jump to a section; locally, follow these same relative links in a Markdown preview.
3. For completion claims, compare the recorded revision and remaining-work notes with [current issues](https://github.com/nickmailhot-lang/StrataAI2/issues) and [CI runs](https://github.com/nickmailhot-lang/StrataAI2/actions/workflows/ci.yml).
4. Return to this index to change subjects, or use the [project README reading paths](../README.md#choose-a-reading-path) for an ordered introduction.

To search the entire folder from the repository root, use `rg -n "search term" docs`. Browse the [full folder on GitHub](https://github.com/nickmailhot-lang/StrataAI2/tree/main/docs) when you need the file layout.

## Browse by subject

- [Planning and dependency order](#planning-and-dependency-order)
- [Runtime, deployment, and operations](#runtime-deployment-and-operations)
- [Identity, profiles, and invitations](#identity-profiles-and-invitations)
- [Organizations and access](#organizations-and-access)
- [Boards and collaboration](#boards-and-collaboration)
- [Lists and Cards](#lists-and-cards)
- [Attachments and file storage](#attachments-and-file-storage)
- [Notifications and activity](#notifications-and-activity)
- [Testing, security, performance, and acceptance](#testing-security-performance-and-acceptance)
- [Shared architecture contracts](#shared-architecture-contracts)

## Planning and dependency order

- [Canonical ticket dependency audit](ticket-dependencies.md)
- [Ticket dependency inventory](ticket-dependency-map.md)

## Runtime, deployment, and operations

- [Durable Organization job foundation (ARCH-07)](architecture/background-jobs.md)
- [Embedded build identity (ARCH-01-AC-002 / ARCH-11)](architecture/build-identity.md)
- [Assembled release payload startup and graceful restart](architecture/release-bundle-startup.md)
- [Configuration and secrets](architecture/configuration.md)
- [Dependency locking (ARCH-01 / ARCH-11)](architecture/dependency-locking.md)
- [Mandatory integration CI groups and coverage guard (ARCH-11)](architecture/integration-ci-groups.md)
- [Initial exact-commit build metadata and release versions (ARCH-11)](architecture/initial-build-metadata.md)
- [API operator metrics export (ARCH-08, partial)](architecture/operator-metrics.md)
- [Runtime database roles](architecture/runtime-database-roles.md)
- [Runtime modes and seeded Demo sign-in credentials](architecture/runtime-modes.md)
- [Schema upgrades and compatibility](architecture/schema-upgrades.md)

## Identity, profiles, and invitations

- [Authentication requirements and acceptance map (PRD-02)](architecture/prd-02-acceptance.md)
- [Session and security-token lifecycle audit clocks (PRD-01 / PRD-02)](architecture/identity-lifecycle-clocks.md)
- [Canonical invitation clocks and revisions (PRD-01 / PRD-03 / PRD-60)](architecture/invitation-audit-metadata.md)
- [Invitation issuer facts, mutable job clocks and remaining repair evidence (PRD-01)](architecture/invitation-issuer-clock-audit.md)
- [Board background ownership and canonical selection clock audit (PRD-01)](architecture/board-background-clock-audit.md)
- [Canonical Board/List/Card/Label route clocks (PRD-01)](architecture/entity-route-clocks.md)

- [Account deactivation and active Organization owners](architecture/account-owner-continuity.md)
- [Identity command retries](architecture/identity-command-retries.md)
- [Identity profile and deactivation transactions](architecture/identity-command-transactions.md)
- [Durable identity email (PRD-02 / ARCH-06 / ARCH-07 / PRD-24)](architecture/identity-email.md)
- [Global identity events](architecture/identity-events.md)
- [Identity realtime recovery](architecture/identity-realtime.md)
- [Administrator invitation creation](architecture/invitation-administration-ui.md)
- [Invitation command transactions](architecture/invitation-command-transactions.md)
- [Retry-safe invitation creation](architecture/invitation-creation-retries.md)
- [Verified-email invitation discovery and acceptance](architecture/invitation-discovery.md)
- [Private invitation recipient source, protected SignalR replay and browser transport contract](architecture/invitation-recipient-events.md)
- [Invitation parent/issuer authority invalidation and bounded Worker dispatch](architecture/invitation-recipient-authority.md)
- [Account profile management (PRD-02)](architecture/profile-management.md)
- [Profile recovery](architecture/profile-recovery.md)
- [Sign-in retry protocol](identity-login-retries.md)
- [Recovery request retries](identity-recovery-request-retries.md)
- [Registration retries](identity-registration-retries.md)
- [Reset and verification completion retries](identity-token-consumption-retries.md)
- [Invitation proof in a request body](invitation-body-acceptance.md)
- [Reconstructable invitation delivery proof](invitation-delivery-tokens.md)
- [Invitation email handler and protected delivery contract](invitation-email-handler.md)
- [Administrator invitation history](invitation-history.md)
- [Recipient invitation links](invitation-link-review.md)
- [Invitation mail recovery fixture](invitation-mail-recovery-fixture.md)
- [Protected invitation mail storage](invitation-mail-store.md)
- [Invitation-backed account registration](invitation-registration.md)

## Organizations and access

- [Organization requirements and acceptance map (PRD-03)](architecture/prd-03-acceptance.md)
- [Organization deletion Worker stages and completion contract (product integration pending)](architecture/organization-deletion-lifecycle.md)
- [Owner deletion request acknowledgments](architecture/organization-deletion-retries.md)
- [Durable Organization creation acknowledgments](architecture/organization-creation-retries.md)
- [Confirmed membership departure and owner continuity](architecture/organization-departure.md)
- [Organization routing integrity](architecture/organization-access-integrity.md)
- [Organization command transactions](architecture/organization-command-transactions.md)
- [Organization and board discovery](architecture/organization-discovery.md)
- [Organization member administration](architecture/organization-member-administration.md)
- [Authorized Organization member directory](architecture/organization-member-directory.md)
- [Membership removal consent](architecture/organization-member-removal-consent.md)
- [Organization metadata settings](architecture/organization-settings.md)
- [Organization metadata event source and remaining delivery work](architecture/organization-metadata-events.md)
- [Authorized metadata replay, opaque cursors and consumer recovery](architecture/organization-metadata-replay.md)

## Boards and collaboration

- [Navigation observations and recovery (PRD-01; implementation in progress)](navigation-observations.md)

- [Persisted board interface](architecture/board-interface.md)
- [Cross-Board Card movement: implementation dependencies](architecture/cross-board-card-movement.md)
- [Work rank allocation](architecture/rank-allocation.md)
- [Board administrator continuity](board-admin-continuity.md)
- [Board archive review and recovery](board-archive-control.md)
- [Board archive discovery](board-archive-discovery.md)
- [Stored Board image ownership](board-background-images.md)
- [Board background selection policy](board-background-policy.md)
- [Independent Board copies](board-copy.md)
- [Board deletion consent](board-deletion-consent.md)
- [Board filtering (PRD-10 / PRD-16)](board-filtering.md)
- [Board invitations — permission foundation](board-invitations.md)
- [Board label command foundation](board-label-api.md)
- [Board label storage](board-label-storage.md)
- [Board member directory authority](board-member-directory.md)
- [Board member administration](board-members-ui.md)
- [Board metadata review and recovery](board-metadata-control.md)
- [Personal Board starring](board-star-preference.md)
- [Board visibility administration](board-visibility-ui.md)
- [Large-Board viewport rendering](board-windowing.md)
- [Keyboard pickup instructions](kanban-pickup-announcement.md)
- [Organization Board directory realtime](organization-board-realtime.md)
- [Public Board link](public-board-sharing.md)

## Lists and Cards

- [Card dates foundation — PRD-12](architecture/card-dates.md)
- [Checklists and items (PRD-13)](architecture/checklists.md)
- [Archived card discovery (PRD-18)](archived-card-directory.md)
- [Archived List discovery (PRD-07 / PRD-18)](archived-list-directory.md)
- [Reviewed active Card archival (PRD-08 / PRD-18)](card-archive-review.md)
- [Card assignment (PRD-11)](card-assignment.md)
- [Card copying](card-copy.md)
- [Permanent Card deletion (PRD-08 / PRD-18)](card-deletion-consent.md)
- [Card movement controls](card-movement-ui.md)
- [List copying](list-copy.md)
- [Permanent List deletion consent (PRD-07 / PRD-18)](list-deletion-consent.md)
- [List position persistence](list-position-persistence.md)
- [List rename review and recovery (PRD-07)](list-rename-persistence.md)

## Attachments and file storage

- [Attachment covers and lifecycle decisions](architecture/attachment-covers-lifecycle.md)
- [Attachment object storage decision and provider contract](architecture/attachment-object-storage.md)
- [Actual scanner engine runtime verification](architecture/attachment-scanner-runtime.md)
- [Explicit local attachment release fixture](attachment-release-fixture.md)

## Notifications and activity

- [PRD-17 notification and watching acceptance map — open](architecture/prd-17-acceptance.md)

- [Assignment notification persistence (PRD-11 / PRD-17)](architecture/assignment-notifications.md)
- [PRD-15 comments, mentions and activity acceptance](architecture/comments-mentions-activity.md)
- [Authorized notification inbox (PRD-17)](architecture/notification-inbox.md)
- [Watch and notification audit clocks](architecture/notification-audit-clocks.md) — stored subscription clocks, notification first-read clocks, observed permission ordering and verification limits.
- [Recipient-private notification events and acceptance evidence](architecture/notification-realtime.md)
- [Watch activity notifications (PRD-17)](architecture/watch-activity-notifications.md)
- [Personal watch subscriptions (PRD-17)](architecture/watch-subscriptions.md)

## Testing, security, performance, and acceptance

- [Source test result artifacts and private-content exclusion (ARCH-11)](architecture/source-test-results.md)
- [Security archive integrity and evidence identity (ARCH-11)](architecture/security-evidence-identity.md)
- [Capacity and diagnostic artifact provenance and layouts (ARCH-11)](architecture/artifact-evidence-provenance.md)
- [Release bundle integrity and tested SBOM preservation (ARCH-11)](architecture/release-artifact-integrity.md)
- [Complete browser suite coverage on isolated runners (ARCH-11)](architecture/browser-shards.md)
- [Activity history observations (PRD-15, partial)](architecture/activity-history-telemetry.md)
- [PRD-14 Attachments and Card Covers acceptance audit](architecture/attachment-acceptance.md)
- [Release browser rate budget](architecture/browser-rate-budget.md)
- [Browser recovery CI investigation](architecture/browser-recovery-ci.md)
- [Same-origin browser request security (PRD-24 / ARCH-06)](architecture/browser-security.md)
- [PRD-13 acceptance audit](architecture/checklist-acceptance.md)
- [Retaining CI evidence for main commits](architecture/ci-run-retention.md)
- [PRD-18 acceptance audit](architecture/lifecycle-acceptance.md)
- [PRD-01 foundation acceptance and closure audit](architecture/prd-01-acceptance.md)
- [PRD-04 acceptance and closure audit](architecture/prd-04-acceptance.md)
- [PRD-05 sharing and permission acceptance audit](architecture/prd-05-acceptance.md)
- [PRD-15 acceptance map — open](architecture/prd-15-acceptance.md)
- [Negative security fixture assertions](architecture/security-test-assertions.md)
- [Board invitation release evidence](board-invitation-release-evidence.md)
- [Board-sharing request telemetry](board-sharing-telemetry.md)
- [Card move browser evidence](card-move-browser-evidence.md)
- [Audited historical release baseline](kanban-green-baseline.md)
- [Kanban performance acceptance](kanban-performance.md)
- [Kanban release evidence](kanban-release-evidence.md)
- [Kanban command telemetry](kanban-telemetry.md)
- [Phone touch movement](kanban-touch-evidence.md)
- [Visibility browser readiness](visibility-browser-readiness.md)

## Shared architecture contracts

- [API host tests (ARCH-01 / ARCH-03 / ARCH-09)](architecture/api-host-testing.md)
- [Shared client Problem boundary (ARCH-02-FR-009)](architecture/client-problems.md)
- [Command actor sessions](architecture/command-actor-sessions.md)
- [Comment read capacity (PRD-15, pending execution)](architecture/comment-capacity.md)
- [Live assignee disclosure and keyboard verification](architecture/live-disclosure-keyboard.md)
- [Bounded PostgreSQL routing discovery (ARCH-04)](architecture/routing-isolation.md)
- [Current Organization surface admission (ARCH-02-AC-003)](architecture/surface-admission.md)
- [Web SPA routing and state boundary](architecture/web-spa-boundary.md)
- [Work Management retry contract](architecture/work-command-retries.md)
- [Work command authorization and lifecycle scopes](architecture/work-command-scopes.md)
- [Work Management command transactions](architecture/work-command-transactions.md)
- [Board synchronization contract (PRD-22)](architecture/work-synchronization.md)

- [Organization event delivery clocks and verification scope](architecture/organization-event-clocks.md)
- [Work event delivery clock audit and remaining provenance gap](architecture/work-event-clock-audit.md)
- [Organization metadata stream clock provenance and guards](architecture/organization-metadata-stream-clocks.md)
- [Invitation recipient stream clocks, provenance and privacy](architecture/invitation-recipient-stream-clocks.md)

## Keeping this index current

When adding a document, link it under the closest subject above and include any related contract or acceptance document in its own introduction. Keep dated evidence and dependency inventories explicit; update links when moving files. Avoid interpreting implementation notes as proof that a whole ticket is complete.
