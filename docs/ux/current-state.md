# Strata workflow audit — 2026-10-17

Current baseline: `e90c6442` (remote main including merged UX foundation PR #94). The initial local checkout `c0943c3b` was 843 commits behind; pre-rebase test evidence is historical only. GitHub PRD issues are authoritative; the dependency inventory is a dated aid, not completion evidence. Reviewed App routes, InternalAppShell, OrganizationHome, BoardScreen, WorkCard/BoardSnapshot, feature controls, source and browser tests, architecture/runtime and acceptance documents, current AGENTS.md and UX-001 through UX-008/feature-acceptance/backlog-map, and the 80 canonical PRD issue bodies (duplicate issue #4 excluded). Synced project `sources/` is empty and unchanged. An existing edit in the primary checkout's WorkManagementRegistration.cs is excluded from this branch.

Implemented below means a concrete implementation exists, not that its parent PRD is complete. Runtime evidence for this branch is recorded separately in delivery-report.md.

| Feature / PRDs | Current entry point | Role | Evidence / status | Problem → correction |
| --- | --- | --- | --- | --- |
| Organizations, boards / 01–05 | `/app`, `/app/:org` | Authenticated member; owner/admin settings | Partial: OrganizationHome, SurfaceAdmission, organization/board APIs and tests | Repeated profile/navigation links and administrative rows → persistent daily navigation and contextual administration |
| Work board / 06–08, 18, 22 | `/app/:org/boards/:board` | Authorized viewer/contributor/admin | Partial: BoardScreen, BoardWindow, CardMoveControls, lifecycle controls, board browser suites | Many controls precede cards; existing active navigation retained → Work destination, secondary disclosure and active feedback |
| Card detail / 09–15, 17–18 | Same board plus `/cards/:card` | Board capabilities control editing | Partial: CardDetailEditor, assignment/date/checklist/attachment/comment controls, archived details | Centered narrow dialog and long action stream → side panel, full screen on phone, organized sections; guard unsaved edits |
| Assignment and dates / 11–12 | Card detail; board previews | Authorized participant | Implemented foundation: version-bound member previews, CardDateEditor, timezone-aware BoardDateProvider | Avatar-only assignment, due state without actual date → bounded names/fallbacks and explicit date |
| Search/filter / 10, 16 | Top-bar search; BoardFilterControl | Current authorized viewer | Partial: personal actor/org/board session criteria, paginated server results and canvas mode | Context loss risk during detail; missing strata dimensions → preserve canvas while detail is open; track missing dimensions separately |
| Activity/notifications / 15, 17, 53–55, 58 | Board/card history; `/notifications` | Authorized participant, recipient-private inbox | Partial: ActivityHistoryControl, NotificationCenterPage; docs/architecture/prd-15-acceptance.md retains gaps | Changes since visit not implemented as a read baseline → keep existing activity and recipient inbox; do not call them a dashboard |
| Units/scope/priority/category / 28–29, 34–35 | No route | Intended authorized strata staff/council | Planned: WorkCard has no affected unit, scope, priority or category fields; no strata lot feature/API | Cannot distinguish affected units from assignees → typed CardStrataLot and explicit scope first; never infer from members or labels |
| Projects / 36 | No route | Intended project participant/manager | Planned: no project feature route or persistence | Do not equate boards with projects → retain distinct Project/Card relationship and independent lifecycle |
| Meetings/agendas/minutes/motions/votes / 37–44, 66 | Disabled sidebar Meetings, no route | Intended eligible meeting roles/voters | Planned: no domain route/feature/API; PRD-43 VOTE-FR-001–008 remains required | Remove dead navigation; enable only with usable authorized workflow; vote inside Motion/meeting |
| Documents / 48–49 | Disabled Documents destination; card attachments exist | Intended permitted document reader/editor | Planned domain; partial attachment foundation under PRD-14 | Attachment ≠ versioned Document/publication → retain attachment return context, defer document destination |
| Vendors / 45–47 | No route | Intended permitted operational/finance roles | Planned domain | Contextual related vendor and settings directory once implemented; no primary entity sprawl |
| Email intake/merge / 65, 72–73, 77–79 | Disabled Correspondence; no workflow | Intended mailbox/triage/review roles | Planned; identity delivery code is not mailbox triage | Work > Email triage; immutable source and reviewed create/link/merge; no invented AI policy |
| Owner portal / 80 | `/portal/:org` | Separately admitted portal actor | Partial shell/publication scope; PortalShell and SurfaceAdmission tests | Preserve separate navigation and server admission; never expose internal work through this redesign |

## Navigation decision

The proposed final hierarchy is Overview, Work, Projects, Meetings, Documents with separate Administration. Only destinations backed by a usable route are exposed. Today Work opens the existing authorized board directory; board routes select Work. Overview is deferred until PRD-54/55 supply a dashboard, rather than showing the same directory twice. Projects, Meetings, Documents and Email triage are deferred until their domain services exist. Organizations, search, account and recipient notifications remain available in the global/context controls. Owner/admin organization settings remain in the existing OrganizationHome action disclosure, using its admitted summary and navigationModel. No shell permission read or duplicate role cache is introduced. Global/account and independently admitted departure/deletion/operational lifecycle, retention policies/holds and public Board exceptions remain intact. Board administration keeps its own `canAdminister` checks; being an organization admin is not substituted for board permissions.

The repository has ordered, user-named Lists, not a canonical `New → In Progress → Done/Closed` enum. Moving a Card changes List; `dueComplete` is independent. Preserve all existing names, IDs, ranks, transitions and archive semantics. Future intake chooses its configured NEW/TRIAGE List under PRD-73; no migration relabels existing lists.

## Official design evidence

Patterns adapted, not competitor branding or feature scope:

- [Asana navigation](https://help.asana.com/s/article/navigating-asana): separate sidebar, global top bar, local header/views and detail pane. Applied to navigation levels and card inspection.
- [Trello project guide](https://trello.com/guide/create-project): concise actionable card titles with supporting information in detail; lists represent a team's workflow. Applied without replacing existing list meaning.
- [Linear board layout](https://linear.app/docs/board-layout): views represent the same issues, with keyboard alternatives and board-local scrolling. No new list/calendar view is introduced to imitate it.
- [Linear display options](https://linear.app/docs/display-options): distinguish personal display choices from shared defaults. Existing actor-scoped session filters stay personal.
- [monday Cards View](https://support.monday.com/hc/en-us/articles/4405723870994-The-Cards-View): cards and table share items, with sort/filter controls. Future alternate views must retain Card identity and authority.

## Exact domain blockers

Unit identification/filtering requires PRD-28 CardStrataLot persistence, object authorization and PRD-34 scope/metadata. Email requires mailbox ingestion/source provenance and reviewed reconciliation under PRD-65/72/73/77/78/79. Meeting/voting requires meeting/agenda/motion/voting policy, eligibility snapshots, audit and confidentiality at both relationship ends. These are implementation dependencies, not requests for routine confirmation. No authority or threshold can be inferred from sidebar design. Existing attachments, comments and identity email do not fulfill these domains.

The current baseline also has Organization type/configuration (PRD-27), operational lifecycle and retention/hold surfaces (PRD-70), safe Markdown preview and identity-aware date invalidation. Preserve them and their recovery paths. Named domain absence observations above are limited to inspected routes/contracts and are not proof that every backend prerequisite is absent.
