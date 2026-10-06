# StrataAI2

StrataAI2 is a strata/condominium governance and operations platform organized around collaborative Boards, Lists, and Cards. Organizations provide the membership and access boundary; Boards organize work into Lists and Cards. The repository contains the React/MUI web application, ASP.NET Core modular monolith API, separate background Worker, PostgreSQL migrations with forced row-level security, private object storage integrations, automated acceptance checks, and implementation documentation.

Development follows the [PRD and architecture backlog](https://github.com/nickmailhot-lang/StrataAI2/issues). Implemented workflows include account and profile management, bounded Organization discovery with direct deep links, membership administration, Board collaboration and lifecycle controls, Lists and Cards, attachments, comments, notifications, and live updates. The browser supports bounded Organization and active Board directories, direct Organization/Board/Card links, and account-bound navigation recovery. Organization creation and Owner deletion requests have account-bound retry contracts. The [deletion request guide](docs/architecture/organization-deletion-retries.md) explains accepted requests and recovery; the [deletion completion contract](docs/architecture/organization-deletion-lifecycle.md) describes retained history, bounded Worker processing, and remaining implementation. Terminal Organization deletion remains incomplete. Invitation creation, recipient review, registration proof, and explicit acceptance have separate documented contracts. Implementation is ongoing: a feature document or passing source check does not mean its full PRD acceptance is complete. Consult the acceptance records and CI for the relevant revision.

## Start here

Jump to [documentation navigation](#navigating-the-documentation), [workflow guides](#find-a-workflow-quickly), [adopted architecture](#adopted-architecture), [repository layout](#repository-layout), or [local source checks](#local-source-checks).

| What you need | Where to start |
| --- | --- |
| Find any document | [Complete documentation index](docs/README.md) |
| Explore implemented workflows | [Boards and collaboration](docs/README.md#boards-and-collaboration), [Lists and Cards](docs/README.md#lists-and-cards), [accounts and invitations](docs/README.md#identity-profiles-and-invitations) |
| Understand the system | [Architecture overview](docs/architecture/README.md), [runtime modes](docs/architecture/runtime-modes.md), [web routing and state](docs/architecture/web-spa-boundary.md) |
| Configure a local or production runtime | [Configuration reference](docs/architecture/configuration.md), [database roles](docs/architecture/runtime-database-roles.md), [schema upgrades](docs/architecture/schema-upgrades.md) |
| Understand background work and live updates | [Durable jobs](docs/architecture/background-jobs.md), [work synchronization](docs/architecture/work-synchronization.md), [Organization Board realtime](docs/organization-board-realtime.md) |
| Run a tested Docker release | [Release bundle guide](docs/release/README.md) |
| Choose the next implementation dependency | [Canonical ticket dependency audit](docs/ticket-dependencies.md), [linked ticket dependency map](docs/ticket-dependency-map.md) |
| Review outstanding acceptance and evidence | [Authentication acceptance](docs/architecture/prd-02-acceptance.md), [Organization acceptance](docs/architecture/prd-03-acceptance.md), [Board acceptance](docs/architecture/prd-04-acceptance.md), [activity acceptance](docs/architecture/prd-15-acceptance.md), [Kanban release evidence](docs/kanban-release-evidence.md), [attachment acceptance](docs/architecture/attachment-acceptance.md) |
| Check current requirements and builds | [Open PRD/architecture issues](https://github.com/nickmailhot-lang/StrataAI2/issues), [CI workflow runs](https://github.com/nickmailhot-lang/StrataAI2/actions/workflows/ci.yml) |

If you are new to the project, follow the architecture and runtime guides before the local checks below. For a feature question, choose a subject in the documentation index and start with its behavior guide. For deployment, start with the release guide and use the configuration and migration references alongside it.

## Navigating the documentation

The [docs index](docs/README.md) links to every document, grouped by subject. Use it when you know a feature but not its filename.

```text
docs/
├── README.md          Complete index: browse every guide by subject
├── architecture/     Architecture overview, runtime and feature contracts
│   └── README.md      Architecture overview and module dependency direction
├── release/
│   └── README.md      Configure and run the Docker release verified by CI
└── *.md              Feature guides, dependency maps, and verification records
```

For a complete inventory, use [Browse by subject](docs/README.md#browse-by-subject); the tables below highlight common entry points. The index includes feature guides, shared contracts, acceptance audits, and historical evidence, including documents not linked individually here.

Open [docs/README.md](docs/README.md), choose **Browse by subject**, and select a descriptive document title. On GitHub, use the document outline to jump between sections; in a local checkout, open `docs/README.md` in your Markdown preview and follow the same relative links. Each guide's relative links lead to related contracts; the index's **project README** link returns here. For the full file inventory on GitHub, open the [docs folder](https://github.com/nickmailhot-lang/StrataAI2/tree/main/docs).

Choose a reading path based on your task:

1. **Understand a feature:** open its subject below, read the behavior guide, then follow its links to permissions, storage, and recovery contracts.
2. **Develop a change:** read the [architecture overview](docs/architecture/README.md), [dependency audit](docs/ticket-dependencies.md), and relevant feature guide; use [API host testing](docs/architecture/api-host-testing.md) and the local checks below to verify it.
3. **Review readiness:** read the feature's acceptance or evidence record, compare its tested revision with [CI](https://github.com/nickmailhot-lang/StrataAI2/actions/workflows/ci.yml), and check the corresponding open issue.
4. **Deploy or operate:** follow the [release guide](docs/release/README.md), [configuration reference](docs/architecture/configuration.md), [database role setup](docs/architecture/runtime-database-roles.md), and [schema upgrade guide](docs/architecture/schema-upgrades.md).

| Documentation topic | Browse the complete section |
| --- | --- |
| Requirements and implementation order | [Planning and dependency order](docs/README.md#planning-and-dependency-order) |
| Configuration, deployment, and monitoring | [Runtime, deployment, and operations](docs/README.md#runtime-deployment-and-operations) |
| Accounts, profiles, and invitations | [Identity, profiles, and invitations](docs/README.md#identity-profiles-and-invitations) |
| Organization membership and permissions | [Organizations and access](docs/README.md#organizations-and-access) |
| Board discovery, administration, and live collaboration | [Boards and collaboration](docs/README.md#boards-and-collaboration) |
| List and Card workflows | [Lists and Cards](docs/README.md#lists-and-cards) |
| Uploads, downloads, covers, and backgrounds | [Attachments and file storage](docs/README.md#attachments-and-file-storage) |
| Personal notifications and activity history | [Notifications and activity](docs/README.md#notifications-and-activity) |
| Verification results and remaining acceptance work | [Testing, security, performance, and acceptance](docs/README.md#testing-security-performance-and-acceptance) |
| Transactions, retries, routing, and other shared contracts | [Shared architecture contracts](docs/README.md#shared-architecture-contracts) |

- **`docs/architecture/`** explains shared contracts, security boundaries, persistence, runtime configuration, background processing, testing, and acceptance gaps. Start with its [overview](docs/architecture/README.md).
- **Documents directly in `docs/`** describe specific Board, List, Card, identity, invitation, and collaboration behavior. They also retain browser evidence, performance notes, and focused implementation decisions.
- **`docs/release/`** explains how to load, configure, migrate, and run the exact images produced by CI.
- **Ticket dependency documents** connect implementation work to the authoritative GitHub requirements. Their dated inventories are snapshots; check current issue state before planning work.

For a feature change, read its behavior document, the related architecture contract, and its acceptance/evidence record. Evidence documents identify what was actually tested and what remains unresolved; older green runs apply to their recorded revision.

Titles containing **acceptance**, **evidence**, or **audit** help assess completion. Titles containing **foundation**, **partial**, or **implementation in progress** describe a limited implemented scope. Read the remaining-work notes before treating either kind of document as a finished feature specification.

The index provides descriptive document titles rather than requiring you to infer a subject from a filename. Links resolve relative to the current README, so they work in GitHub and a local Markdown viewer. To find a term across the documentation from the repository root, use `rg -n "search term" docs`. When adding a document, add it to the appropriate subject in [the index](docs/README.md).

Useful routes through the docs include:

- **Organization administration:** [current access](docs/architecture/organization-access-integrity.md) → [settings and metadata recovery](docs/architecture/organization-settings.md) → [member administration](docs/architecture/organization-member-administration.md) → [departure and owner continuity](docs/architecture/organization-departure.md) → [deletion request recovery](docs/architecture/organization-deletion-retries.md) → [deletion completion contract](docs/architecture/organization-deletion-lifecycle.md) → [Organization acceptance](docs/architecture/prd-03-acceptance.md).
- **Board collaboration:** [Board interface](docs/architecture/board-interface.md) → [windowing](docs/board-windowing.md) → [work synchronization](docs/architecture/work-synchronization.md) → [Organization Board realtime](docs/organization-board-realtime.md) → [Board acceptance](docs/architecture/prd-04-acceptance.md).
- **Safe writes and recovery:** [command transactions](docs/architecture/work-command-transactions.md) → [command scopes](docs/architecture/work-command-scopes.md) → [command retries](docs/architecture/work-command-retries.md) → [current actor sessions](docs/architecture/command-actor-sessions.md).
- **Files and backgrounds:** [object storage](docs/architecture/attachment-object-storage.md) → [attachment acceptance](docs/architecture/attachment-acceptance.md) → [Board background images](docs/board-background-images.md) → [cover lifecycle](docs/architecture/attachment-covers-lifecycle.md).
- **Search and filters:** [Board filtering and private search interaction sources](docs/board-filtering.md) → [Organization access integrity](docs/architecture/organization-access-integrity.md) → [work synchronization](docs/architecture/work-synchronization.md). The filtering guide covers global search, Board criteria, private interaction acknowledgments, and remaining acceptance work.
- **Operating a release:** [release guide](docs/release/README.md) → [configuration](docs/architecture/configuration.md) → [schema upgrades](docs/architecture/schema-upgrades.md) → [operator metrics](docs/architecture/operator-metrics.md).
- **Checking build evidence:** [build identity](docs/architecture/build-identity.md) → [CI evidence retention](docs/architecture/ci-run-retention.md) → [browser recovery checks](docs/architecture/browser-recovery-ci.md) → [Kanban release evidence](docs/kanban-release-evidence.md). Match the application revision to the retained results before relying on a release claim.

### Find a workflow quickly

These links open the behavior or contract guide directly. Use the subject sections in [the complete index](docs/README.md) for related implementation notes and evidence.

| Workflow | Primary guides |
| --- | --- |
| Sign in, recover an account, or manage a profile | [Authentication requirements and acceptance map](docs/architecture/prd-02-acceptance.md), [sign-in recovery and retries](docs/identity-login-retries.md), [recovery requests](docs/identity-recovery-request-retries.md), [profile management and local-time display](docs/architecture/profile-management.md) |
| Verify an email, reset a password, sign out, or deactivate an account | [Single-use verification and reset tokens](docs/identity-token-consumption-retries.md), [logout and deactivation retries](docs/architecture/identity-command-retries.md), [account ownership continuity](docs/architecture/account-owner-continuity.md) |
| Browse Organizations, follow deep links, and manage settings | [Paged Organization directory and browser navigation](docs/architecture/organization-discovery.md), [current membership and canonical Organization reads](docs/architecture/organization-access-integrity.md), [Organization settings](docs/architecture/organization-settings.md) |
| Create an Organization or recover a lost creation response | [Organization creation and original acknowledgments](docs/architecture/organization-creation-retries.md), [current Organization discovery](docs/architecture/organization-discovery.md), [Organization acceptance](docs/architecture/prd-03-acceptance.md) |
| Request Organization deletion and recover its acknowledgment | [Owner deletion request and retry contract](docs/architecture/organization-deletion-retries.md), [pending deletion completion and retention contract](docs/architecture/organization-deletion-lifecycle.md), [lifecycle acceptance](docs/architecture/lifecycle-acceptance.md), [Organization acceptance](docs/architecture/prd-03-acceptance.md). A successful request acknowledgment does not confirm completed deletion. |
| Register from an invitation and accept access | [Invitation-backed registration](docs/invitation-registration.md), [registration retries](docs/identity-registration-retries.md), [verified-email discovery and acceptance](docs/architecture/invitation-discovery.md) |
| Review Organization requirements and membership changes | [Organization acceptance map](docs/architecture/prd-03-acceptance.md), [member administration](docs/architecture/organization-member-administration.md), [membership removal consent](docs/architecture/organization-member-removal-consent.md), [confirmed departure and durable API acknowledgments](docs/architecture/organization-departure.md) |
| Invite people and manage access | [Invitation administration](docs/architecture/invitation-administration-ui.md), [recipient invitation review](docs/invitation-link-review.md), [Organization members](docs/architecture/organization-member-administration.md), [Board members](docs/board-members-ui.md) |
| Browse active and archived Boards | [Paged active Board directory](docs/architecture/organization-discovery.md#browser-active-board-paging), [archived Board discovery](docs/board-archive-discovery.md) |
| Find, filter, share, or copy a Board | [Search and filtering](docs/board-filtering.md), [public sharing](docs/public-board-sharing.md), [Board copies](docs/board-copy.md) |
| Navigate between Organizations, Boards, and Cards | [Navigation observations and recovery](docs/navigation-observations.md), [web routing and state](docs/architecture/web-spa-boundary.md), [Organization routing integrity](docs/architecture/organization-access-integrity.md) |
| Understand rollback and retry behavior | [Identity transactions](docs/architecture/identity-command-transactions.md), [Organization transactions](docs/architecture/organization-command-transactions.md), [invitation transactions](docs/architecture/invitation-command-transactions.md), [Work transactions](docs/architecture/work-command-transactions.md), [Work retries](docs/architecture/work-command-retries.md) |
| Move and organize work | [Card movement](docs/card-movement-ui.md), [List ordering](docs/list-position-persistence.md), [large-Board rendering](docs/board-windowing.md) |
| Assign work, set dates, and track checklists | [Assignments](docs/card-assignment.md), [Card dates](docs/architecture/card-dates.md), [checklists](docs/architecture/checklists.md) |
| Upload files and choose Card covers or Board backgrounds | [Private object storage](docs/architecture/attachment-object-storage.md), [cover lifecycle](docs/architecture/attachment-covers-lifecycle.md), [Board background images](docs/board-background-images.md), [attachment acceptance](docs/architecture/attachment-acceptance.md) |
| Follow discussions and notifications | [Comments, mentions, and activity](docs/architecture/comments-mentions-activity.md), [notification inbox](docs/architecture/notification-inbox.md), [watch subscriptions](docs/architecture/watch-subscriptions.md) |
| Archive, restore, or delete work | [Board archive controls](docs/board-archive-control.md), [archived Lists](docs/archived-list-directory.md), [archived Cards](docs/archived-card-directory.md), [lifecycle acceptance](docs/architecture/lifecycle-acceptance.md) |

For each workflow, read the guide's scope and remaining-work notes first. Follow its contract links for implementation details, then consult the acceptance record and the CI run for the revision you are reviewing. The documentation is organized by subject rather than numbered ticket order; use the [dependency map](docs/ticket-dependency-map.md) to connect a subject to its PRD or architecture ticket.

The [navigation guide](docs/navigation-observations.md) describes account-bound context changes and Board/Card opens, lost-response recovery, and current access checks. Its verification section identifies the source, database, container, and browser checks that must be reviewed before claiming PRD-01 acceptance.

### Documentation conventions

Use [the complete index](docs/README.md#browse-by-subject) for **every document**, including guides that are not linked directly from this README. Feature guides also live in `docs/architecture/`; the folder name alone does not distinguish a workflow from a shared contract.

| Document type | What it tells you | How to use it |
| --- | --- | --- |
| Behavior or workflow guide | Implemented actions, permissions, request contracts, and recovery behavior | Start here when changing or using a feature. |
| Architecture contract | Module boundaries, transaction ownership, storage, security, and runtime decisions | Read alongside the feature guide before changing its implementation. |
| Acceptance audit | Requirement coverage and remaining work | Compare with the current PRD/ARCH issue before deciding a ticket is complete. |
| Evidence record | Tests and observations for a specific revision and environment | Match its commit to the CI run and release artifacts you are reviewing. |
| Dependency inventory | Ticket relationships and a dated backlog snapshot | Use it to choose implementation order, then verify current issue status. |

Demo checks, restricted PostgreSQL checks, and checks against the final Docker images establish different parts of acceptance. A passing source check or an implemented workflow alone does not establish release readiness.

For account and search changes, read [profile management and local-time display](docs/architecture/profile-management.md), [identity transactions and final session admission](docs/architecture/identity-command-transactions.md), [identity realtime recovery](docs/architecture/identity-realtime.md), and [search and filtering](docs/board-filtering.md). These guides explain how current account preferences, Board date policies, and access checks affect the returned data and browser display.

For directory and access changes, read [Organization browser paging and deep links](docs/architecture/organization-discovery.md#browser-paging-and-direct-organization-admission), [active Board paging](docs/architecture/organization-discovery.md#browser-active-board-paging), [current Organization access](docs/architecture/organization-access-integrity.md), and [Organization transaction rollback](docs/architecture/organization-command-transactions.md#demo-rollback-and-final-actor-admission).

When adding or moving documentation, follow [index maintenance](docs/README.md#keeping-this-index-current): keep the subject index exhaustive and update links in related guides. GitHub issues remain the source for current ticket status; read historical evidence with its recorded date and revision.

## Adopted architecture

StrataAI2 is a modular monolith delivered as three application processes:

- **Web:** React, TypeScript, Vite, and MUI.
- **API:** ASP.NET Core, with Domain, Application, and Infrastructure layers.
- **Worker:** a separate .NET process sharing the same domain and application contracts.

PostgreSQL is the primary datastore, with forced row-level security for Organization isolation, pgvector for retrieval capabilities, and transactional durable jobs. File binaries use private object storage behind application interfaces; see the [storage decision](docs/architecture/attachment-object-storage.md). SignalR provides authorized live collaboration. CI builds immutable web/API/Worker images once and verifies those images before assembling a release bundle.

The canonical work hierarchy is `Organization → Board → List → Card`. Users join Organizations; `Organization` is the tenant boundary represented by `tenant_id` in persistence. See [Organization access integrity](docs/architecture/organization-access-integrity.md) and [routing isolation](docs/architecture/routing-isolation.md).

## Repository layout

| Path | Contents |
| --- | --- |
| [`apps/web/`](apps/web/) | React/MUI application and component tests |
| [`src/StrataAI.Domain/`](src/StrataAI.Domain/) | Domain entities and rules |
| [`src/StrataAI.Application/`](src/StrataAI.Application/) | Use cases and application contracts |
| [`src/StrataAI.Infrastructure/`](src/StrataAI.Infrastructure/) | Persistence and provider adapters |
| [`src/StrataAI.Api/`](src/StrataAI.Api/) | HTTP, authentication, and realtime host |
| [`src/StrataAI.Worker/`](src/StrataAI.Worker/) | Background processing host |
| [`db/`](db/) | Ordered migrations and restricted runtime role provisioning |
| [`tests/`](tests/) | Domain, API-host, restricted persistence, and browser acceptance checks |
| [`docs/`](docs/) | [Documentation index and subject guides](docs/README.md) |
| [`scripts/`](scripts/) | Release, operational, and CI verification scripts |
| [`.github/workflows/`](.github/workflows/) | [Build-once CI](.github/workflows/ci.yml) and repository automation |

## Local source checks

Use the .NET 10 SDK, Node.js 24, and the npm version declared in [package.json](package.json). Docker Engine/Desktop and Compose v2 are needed for container runtime checks.

From a clean checkout at the repository root:

```sh
npm ci
npm run typecheck
npm run typecheck:browser
npm run lint
npm test
npm run build
dotnet restore StrataAI2.slnx --locked-mode
dotnet build StrataAI2.slnx --configuration Release --no-restore -warnaserror
dotnet test tests/StrataAI.Domain.Tests/StrataAI.Domain.Tests.csproj --configuration Release --no-build
dotnet test tests/StrataAI.Api.Tests/StrataAI.Api.Tests.csproj --configuration Release --no-build
```

API-host tests select an isolated Demo runtime without production database or provider credentials. Read [API host testing](docs/architecture/api-host-testing.md) and [dependency locking](docs/architecture/dependency-locking.md) for the test boundaries and locked dependency workflow.

These source checks are only part of release validation. [CI runs](https://github.com/nickmailhot-lang/StrataAI2/actions/workflows/ci.yml) also verify real PostgreSQL/RLS behavior, restricted runtime capabilities, exact-image integration, native browser workflows, and security evidence. Deploy the tested artifacts using the [release bundle guide](docs/release/README.md).
