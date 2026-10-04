# Canonical ticket dependency audit

Snapshot: 2026-10-02. Source: GitHub issue bodies and states, fetched from the repository's issue API. This covers all 80 PRDs and 12 architecture tickets, excluding duplicate PRD-03 issue #4. There are 91 open canonical issues; ARCH-01 (#82) is closed. Issue state is inventory evidence, not a requirement-by-requirement completion audit.

Every canonical issue has a Dependencies section. All extracted PRD/ARCH references resolve to canonical issues. No issue was closed by this audit.

## Dependency groups

A dependency-first traversal of strongly connected components produces the groups below. Within a cycle there is no whole-ticket topological order: implement shared contracts and producer/consumer slices sequentially, then verify each ticket's complete acceptance criteria. The grouping does not waive a dependency, acceptance criterion, or adopted architecture requirement.

1. PRD-01, PRD-02, PRD-03, PRD-04, PRD-05, PRD-06, PRD-07, PRD-08, PRD-09, PRD-10, PRD-11, PRD-12, PRD-13, PRD-14, PRD-15, PRD-16, PRD-17, PRD-18, PRD-20, PRD-21, PRD-22, PRD-23, PRD-24, PRD-25 (cycle)
2. PRD-19
3. PRD-27
4. PRD-28, PRD-29, PRD-30, PRD-31, PRD-32, PRD-33, PRD-34, PRD-35, PRD-36, PRD-37, PRD-38, PRD-39, PRD-40, PRD-41, PRD-42, PRD-43, PRD-44, PRD-45, PRD-46, PRD-47, PRD-48, PRD-49, PRD-50, PRD-51, PRD-52, PRD-53, PRD-54, PRD-55, PRD-56, PRD-57, PRD-58, PRD-59, PRD-60, PRD-61, PRD-62, PRD-63, PRD-64, PRD-65, PRD-66, PRD-67, PRD-68, PRD-69, PRD-70, PRD-71, PRD-72, PRD-73, PRD-74, PRD-75, PRD-76, PRD-77, PRD-78, PRD-79, PRD-80 (cycle)
5. PRD-26
6. ARCH-01
7. ARCH-03, ARCH-04, ARCH-05, ARCH-06, ARCH-07, ARCH-08, ARCH-09, ARCH-10, ARCH-11, ARCH-12 (cycle)
8. ARCH-02

The architecture foundation is already adopted and is being implemented alongside its dependent product contracts. Group order describes the issue graph, not an instruction to discard or rebuild that foundation.

In particular, PRD-11 depends on PRD-16 and PRD-17; PRD-17 depends on PRD-11, PRD-12, PRD-15 and PRD-22; PRD-12 depends on PRD-17. Assignment persistence, editing, previews and member filtering are producer slices. Atomic assignment recipient intent, authorized inbox/read commands, watches, personal due Reminders and their MUI controls now have implementations and scoped tests. Exact-image Reminder delivery passed its notification/receipt checks before the fixture's readiness assertion failed; the corrected complete runtime gate remains pending. Mentions, remaining notification requirements and PRD-wide lifecycle/performance acceptance are still unfinished. Existing content-free Work invalidation events do not prove notification delivery. A fresh issue inventory still records 91 open canonical tickets; this progress does not close them.

## Complete canonical inventory

Dependencies are copied from the issue's Dependencies section, sorted and deduplicated. A closed state below records GitHub's state only; it does not substitute for the final completion audit.

| Ticket | GitHub issue | State | Declared dependencies |
| --- | --- | --- | --- |
| ARCH-01 | [[ARCH-01] Repository, Solution and Technology Baseline](https://github.com/nickmailhot-lang/StrataAI2/issues/82) | closed | PRD-01, PRD-19, PRD-21, PRD-25, PRD-26 |
| ARCH-02 | [[ARCH-02] Web SPA, Nginx Edge and Front-End Module Architecture](https://github.com/nickmailhot-lang/StrataAI2/issues/83) | open | ARCH-01, ARCH-03, ARCH-06, ARCH-10, PRD-80 |
| ARCH-03 | [[ARCH-03] ASP.NET Core Modular-Monolith API Architecture](https://github.com/nickmailhot-lang/StrataAI2/issues/84) | open | ARCH-04, ARCH-05, ARCH-06, ARCH-07 |
| ARCH-04 | [[ARCH-04] PostgreSQL Persistence, Tenant Isolation and Schema Evolution](https://github.com/nickmailhot-lang/StrataAI2/issues/85) | open | ARCH-03, ARCH-06, ARCH-09, ARCH-12, PRD-20, PRD-70 |
| ARCH-05 | [[ARCH-05] Runtime Modes, Provider Abstractions and Demo Isolation](https://github.com/nickmailhot-lang/StrataAI2/issues/86) | open | ARCH-03, ARCH-04, ARCH-07, ARCH-10 |
| ARCH-06 | [[ARCH-06] Authentication, Authorization and Shared Security Boundary](https://github.com/nickmailhot-lang/StrataAI2/issues/87) | open | ARCH-04, ARCH-07, PRD-02, PRD-24, PRD-35, PRD-80 |
| ARCH-07 | [[ARCH-07] Integrations, AI, Email and Background Processing Architecture](https://github.com/nickmailhot-lang/StrataAI2/issues/88) | open | ARCH-03, ARCH-04, ARCH-06, ARCH-08, PRD-72, PRD-79 |
| ARCH-08 | [[ARCH-08] Observability, Health, Audit and Operational Resilience](https://github.com/nickmailhot-lang/StrataAI2/issues/89) | open | ARCH-04, ARCH-07, ARCH-10, ARCH-11, PRD-53, PRD-70 |
| ARCH-09 | [[ARCH-09] Automated Test Architecture and Engineering Quality Gates](https://github.com/nickmailhot-lang/StrataAI2/issues/90) | open | ARCH-01, ARCH-04, ARCH-06, ARCH-10, ARCH-11 |
| ARCH-10 | [[ARCH-10] Docker Images, Compose Runtime and Local/Hosted Execution](https://github.com/nickmailhot-lang/StrataAI2/issues/91) | open | ARCH-01, ARCH-03, ARCH-04, ARCH-05, ARCH-11 |
| ARCH-11 | [[ARCH-11] Rigorous CI, Immutable Containers and Release Artifacts](https://github.com/nickmailhot-lang/StrataAI2/issues/92) | open | ARCH-01, ARCH-03, ARCH-04, ARCH-07, ARCH-08, ARCH-09, ARCH-10, ARCH-12 |
| ARCH-12 | [[ARCH-12] Configuration, Secrets, Environments, Promotion and Rollback](https://github.com/nickmailhot-lang/StrataAI2/issues/93) | open | ARCH-04, ARCH-08, ARCH-11, PRD-24, PRD-70 |
| PRD-01 | [[PRD-01] Product Foundation and Information Architecture](https://github.com/nickmailhot-lang/StrataAI2/issues/1) | open | PRD-02, PRD-03, PRD-04, PRD-06, PRD-08, PRD-09 |
| PRD-02 | [[PRD-02] Authentication and User Accounts](https://github.com/nickmailhot-lang/StrataAI2/issues/2) | open | PRD-24 |
| PRD-03 | [[PRD-03] Organizations and Organization Membership](https://github.com/nickmailhot-lang/StrataAI2/issues/3) | open | PRD-02, PRD-05, PRD-18 |
| PRD-04 | [[PRD-04] Board Creation and Board Management](https://github.com/nickmailhot-lang/StrataAI2/issues/5) | open | PRD-03, PRD-05, PRD-18 |
| PRD-05 | [[PRD-05] Board Sharing, Visibility, Roles and Permissions](https://github.com/nickmailhot-lang/StrataAI2/issues/6) | open | PRD-02, PRD-03, PRD-04, PRD-24 |
| PRD-06 | [[PRD-06] Kanban Board Interface and Drag-and-Drop Interaction](https://github.com/nickmailhot-lang/StrataAI2/issues/7) | open | PRD-07, PRD-08, PRD-22, PRD-23 |
| PRD-07 | [[PRD-07] List Management](https://github.com/nickmailhot-lang/StrataAI2/issues/8) | open | PRD-04, PRD-06, PRD-08, PRD-17, PRD-18 |
| PRD-08 | [[PRD-08] Card Creation, Movement and Lifecycle](https://github.com/nickmailhot-lang/StrataAI2/issues/9) | open | PRD-06, PRD-09, PRD-18, PRD-22 |
| PRD-09 | [[PRD-09] Card Detail Experience](https://github.com/nickmailhot-lang/StrataAI2/issues/10) | open | PRD-08, PRD-10, PRD-11, PRD-12, PRD-13, PRD-14, PRD-15, PRD-17 |
| PRD-10 | [[PRD-10] Labels and Categorization](https://github.com/nickmailhot-lang/StrataAI2/issues/11) | open | PRD-04, PRD-08, PRD-16 |
| PRD-11 | [[PRD-11] Members and Card Assignment](https://github.com/nickmailhot-lang/StrataAI2/issues/12) | open | PRD-05, PRD-08, PRD-16, PRD-17 |
| PRD-12 | [[PRD-12] Start Dates, Due Dates, Reminders and Completion](https://github.com/nickmailhot-lang/StrataAI2/issues/13) | open | PRD-08, PRD-17, PRD-24 |
| PRD-13 | [[PRD-13] Checklists and Checklist Items](https://github.com/nickmailhot-lang/StrataAI2/issues/14) | open | PRD-08, PRD-22 |
| PRD-14 | [[PRD-14] Attachments and Card Covers](https://github.com/nickmailhot-lang/StrataAI2/issues/15) | open | PRD-08, PRD-24 |
| PRD-15 | [[PRD-15] Comments, Mentions and Activity History](https://github.com/nickmailhot-lang/StrataAI2/issues/16) | open | PRD-08, PRD-17, PRD-24 |
| PRD-16 | [[PRD-16] Search and Board Filtering](https://github.com/nickmailhot-lang/StrataAI2/issues/17) | open | PRD-10, PRD-11, PRD-12, PRD-18, PRD-24 |
| PRD-17 | [[PRD-17] Notifications and Watching](https://github.com/nickmailhot-lang/StrataAI2/issues/18) | open | PRD-11, PRD-12, PRD-15, PRD-22 |
| PRD-18 | [[PRD-18] Archiving, Restoration, Permanent Deletion and Audit Integrity](https://github.com/nickmailhot-lang/StrataAI2/issues/19) | open | PRD-04, PRD-07, PRD-08, PRD-15, PRD-24 |
| PRD-19 | [[PRD-19] Cross-Cutting Non-Functional Requirements](https://github.com/nickmailhot-lang/StrataAI2/issues/20) | open | PRD-01, PRD-22, PRD-25 |
| PRD-20 | [[PRD-20] Application Data Model](https://github.com/nickmailhot-lang/StrataAI2/issues/21) | open | PRD-01, PRD-18 |
| PRD-21 | [[PRD-21] API Contract and Service Architecture](https://github.com/nickmailhot-lang/StrataAI2/issues/22) | open | PRD-20, PRD-24 |
| PRD-22 | [[PRD-22] Real-Time Collaboration and Concurrency](https://github.com/nickmailhot-lang/StrataAI2/issues/23) | open | PRD-06, PRD-08, PRD-21, PRD-24 |
| PRD-23 | [[PRD-23] Accessibility and Keyboard Interaction](https://github.com/nickmailhot-lang/StrataAI2/issues/24) | open | PRD-06, PRD-09, PRD-25 |
| PRD-24 | [[PRD-24] Security, Privacy and Authorization](https://github.com/nickmailhot-lang/StrataAI2/issues/25) | open | PRD-02, PRD-05, PRD-14, PRD-21, PRD-22, PRD-25 |
| PRD-25 | [[PRD-25] Testing and Definition of Done](https://github.com/nickmailhot-lang/StrataAI2/issues/26) | open | PRD-01, PRD-24 |
| PRD-26 | [[PRD-26] MVP Release and Implementation Plan](https://github.com/nickmailhot-lang/StrataAI2/issues/27) | open | PRD-01, PRD-25, PRD-27, PRD-80 |
| PRD-27 | [[PRD-27] Organization Types and Strata Organization Configuration](https://github.com/nickmailhot-lang/StrataAI2/issues/28) | open | PRD-01, PRD-03, PRD-20, PRD-24 |
| PRD-28 | [[PRD-28] Strata Lots / Units and Common Property](https://github.com/nickmailhot-lang/StrataAI2/issues/29) | open | PRD-27, PRD-34, PRD-61, PRD-62 |
| PRD-29 | [[PRD-29] Organization Membership and Unit Associations](https://github.com/nickmailhot-lang/StrataAI2/issues/30) | open | PRD-03, PRD-24, PRD-60, PRD-61, PRD-80 |
| PRD-30 | [[PRD-30] Council Terms and Historical Governance](https://github.com/nickmailhot-lang/StrataAI2/issues/31) | open | PRD-27, PRD-31, PRD-37, PRD-43 |
| PRD-31 | [[PRD-31] President, Vice-President and Council Governance Roles](https://github.com/nickmailhot-lang/StrataAI2/issues/32) | open | PRD-30, PRD-35, PRD-38, PRD-43 |
| PRD-32 | [[PRD-32] Strata Manager and Acting-on-Behalf-of-Strata](https://github.com/nickmailhot-lang/StrataAI2/issues/33) | open | PRD-31, PRD-33, PRD-35, PRD-53, PRD-67 |
| PRD-33 | [[PRD-33] Property Management Companies and Manager Assignments](https://github.com/nickmailhot-lang/StrataAI2/issues/34) | open | PRD-32, PRD-48, PRD-67 |
| PRD-34 | [[PRD-34] Strata Card Metadata: Priority, Category, Scope and Source](https://github.com/nickmailhot-lang/StrataAI2/issues/35) | open | PRD-08, PRD-10, PRD-28, PRD-35 |
| PRD-35 | [[PRD-35] Card Confidentiality and Object-Level Permissions](https://github.com/nickmailhot-lang/StrataAI2/issues/36) | open | PRD-05, PRD-24, PRD-53, PRD-56, PRD-72, PRD-80 |
| PRD-36 | [[PRD-36] Projects and Project Work Management](https://github.com/nickmailhot-lang/StrataAI2/issues/37) | open | PRD-34, PRD-44, PRD-45, PRD-69, PRD-77 |
| PRD-37 | [[PRD-37] Meetings and Meeting Lifecycle](https://github.com/nickmailhot-lang/StrataAI2/issues/38) | open | PRD-31, PRD-38, PRD-39, PRD-40, PRD-66 |
| PRD-38 | [[PRD-38] Meeting Attendance and Roles](https://github.com/nickmailhot-lang/StrataAI2/issues/39) | open | PRD-31, PRD-37, PRD-40, PRD-43, PRD-66 |
| PRD-39 | [[PRD-39] Agendas and Agenda Items](https://github.com/nickmailhot-lang/StrataAI2/issues/40) | open | PRD-37, PRD-41, PRD-48, PRD-65 |
| PRD-40 | [[PRD-40] Meeting Minutes, Minute Sections and Approval](https://github.com/nickmailhot-lang/StrataAI2/issues/41) | open | PRD-37, PRD-38, PRD-39, PRD-41, PRD-74, PRD-77, PRD-80 |
| PRD-41 | [[PRD-41] Card / Meeting / Agenda / Minutes Relationships](https://github.com/nickmailhot-lang/StrataAI2/issues/42) | open | PRD-08, PRD-39, PRD-40, PRD-44, PRD-57, PRD-78 |
| PRD-42 | [[PRD-42] Motions](https://github.com/nickmailhot-lang/StrataAI2/issues/43) | open | PRD-37, PRD-39, PRD-43, PRD-44, PRD-72 |
| PRD-43 | [[PRD-43] Voting and Voting Thresholds](https://github.com/nickmailhot-lang/StrataAI2/issues/44) | open | PRD-31, PRD-38, PRD-42, PRD-44, PRD-66 |
| PRD-44 | [[PRD-44] Resolutions and Decision Follow-Up](https://github.com/nickmailhot-lang/StrataAI2/issues/45) | open | PRD-36, PRD-42, PRD-43, PRD-69, PRD-77 |
| PRD-45 | [[PRD-45] Vendors and Vendor Contacts](https://github.com/nickmailhot-lang/StrataAI2/issues/46) | open | PRD-36, PRD-46, PRD-47, PRD-58, PRD-71 |
| PRD-46 | [[PRD-46] Quotes and Procurement Records](https://github.com/nickmailhot-lang/StrataAI2/issues/47) | open | PRD-45, PRD-48, PRD-69, PRD-76, PRD-77 |
| PRD-47 | [[PRD-47] Invoices and Cost Records](https://github.com/nickmailhot-lang/StrataAI2/issues/48) | open | PRD-45, PRD-46, PRD-69, PRD-76, PRD-77 |
| PRD-48 | [[PRD-48] Strata Document Management](https://github.com/nickmailhot-lang/StrataAI2/issues/49) | open | PRD-14, PRD-35, PRD-49, PRD-70, PRD-80 |
| PRD-49 | [[PRD-49] Document Relationships and Versioning](https://github.com/nickmailhot-lang/StrataAI2/issues/50) | open | PRD-48, PRD-50, PRD-70, PRD-80 |
| PRD-50 | [[PRD-50] Bylaws, Bylaw Versions and Rules](https://github.com/nickmailhot-lang/StrataAI2/issues/51) | open | PRD-44, PRD-48, PRD-49, PRD-64, PRD-75 |
| PRD-51 | [[PRD-51] Owner / Resident Issue Intake](https://github.com/nickmailhot-lang/StrataAI2/issues/52) | open | PRD-28, PRD-52, PRD-64, PRD-65, PRD-80 |
| PRD-52 | [[PRD-52] Issue-to-Card and Issue Escalation Workflow](https://github.com/nickmailhot-lang/StrataAI2/issues/53) | open | PRD-08, PRD-41, PRD-51, PRD-64, PRD-78, PRD-80 |
| PRD-53 | [[PRD-53] Strata Audit Trail and Historical Attribution](https://github.com/nickmailhot-lang/StrataAI2/issues/54) | open | PRD-15, PRD-24, PRD-32, PRD-70, PRD-72 |
| PRD-54 | [[PRD-54] StrataAI Operational Dashboard](https://github.com/nickmailhot-lang/StrataAI2/issues/55) | open | PRD-34, PRD-36, PRD-37, PRD-58, PRD-72 |
| PRD-55 | [[PRD-55] Role-Specific Dashboards](https://github.com/nickmailhot-lang/StrataAI2/issues/56) | open | PRD-31, PRD-54, PRD-67 |
| PRD-56 | [[PRD-56] StrataAI Global Search](https://github.com/nickmailhot-lang/StrataAI2/issues/57) | open | PRD-16, PRD-24, PRD-35, PRD-75, PRD-80 |
| PRD-57 | [[PRD-57] Related Records and Relationship Navigation](https://github.com/nickmailhot-lang/StrataAI2/issues/58) | open | PRD-20, PRD-41, PRD-49, PRD-78 |
| PRD-58 | [[PRD-58] StrataAI Notifications and Watching](https://github.com/nickmailhot-lang/StrataAI2/issues/59) | open | PRD-17, PRD-35, PRD-54, PRD-80 |
| PRD-59 | [[PRD-59] StrataAI Operational Reporting](https://github.com/nickmailhot-lang/StrataAI2/issues/60) | open | PRD-53, PRD-54, PRD-56, PRD-67, PRD-70 |
| PRD-60 | [[PRD-60] User Onboarding, Invitations and Account Lifecycle](https://github.com/nickmailhot-lang/StrataAI2/issues/61) | open | PRD-02, PRD-03, PRD-24, PRD-80 |
| PRD-61 | [[PRD-61] Owners, Residents, Occupants and Strata-Lot Relationship History](https://github.com/nickmailhot-lang/StrataAI2/issues/62) | open | PRD-28, PRD-29, PRD-35, PRD-70, PRD-80 |
| PRD-62 | [[PRD-62] Property, Buildings, Common Areas and Asset Registry](https://github.com/nickmailhot-lang/StrataAI2/issues/63) | open | PRD-27, PRD-28, PRD-57, PRD-63, PRD-68 |
| PRD-63 | [[PRD-63] Maintenance, Inspections, Recurring Work and Asset Service History](https://github.com/nickmailhot-lang/StrataAI2/issues/64) | open | PRD-12, PRD-34, PRD-45, PRD-62, PRD-71 |
| PRD-64 | [[PRD-64] Bylaw Enforcement and Compliance Cases](https://github.com/nickmailhot-lang/StrataAI2/issues/65) | open | PRD-35, PRD-50, PRD-51, PRD-65, PRD-77 |
| PRD-65 | [[PRD-65] Correspondence, Notices and Delivery Tracking](https://github.com/nickmailhot-lang/StrataAI2/issues/66) | open | PRD-35, PRD-48, PRD-58, PRD-70, PRD-77, PRD-79 |
| PRD-66 | [[PRD-66] AGM / SGM Notice, Quorum, Proxies and General-Meeting Voting](https://github.com/nickmailhot-lang/StrataAI2/issues/67) | open | PRD-37, PRD-38, PRD-42, PRD-43, PRD-70 |
| PRD-67 | [[PRD-67] Multi-Strata Portfolio Management](https://github.com/nickmailhot-lang/StrataAI2/issues/68) | open | PRD-24, PRD-32, PRD-33, PRD-55, PRD-56 |
| PRD-68 | [[PRD-68] Incident, Damage and Insurance Claim Management](https://github.com/nickmailhot-lang/StrataAI2/issues/69) | open | PRD-28, PRD-34, PRD-36, PRD-45, PRD-69 |
| PRD-69 | [[PRD-69] Financial Authorization, Budgets and Funding Context](https://github.com/nickmailhot-lang/StrataAI2/issues/70) | open | PRD-36, PRD-44, PRD-46, PRD-47, PRD-77 |
| PRD-70 | [[PRD-70] Records Retention, Privacy, Redaction, Legal Hold and Data Export](https://github.com/nickmailhot-lang/StrataAI2/issues/71) | open | PRD-18, PRD-24, PRD-48, PRD-53, PRD-59 |
| PRD-71 | [[PRD-71] Calendar, Recurrence and Operational Deadlines](https://github.com/nickmailhot-lang/StrataAI2/issues/72) | open | PRD-12, PRD-37, PRD-58, PRD-63 |
| PRD-72 | [[PRD-72] StrataAI AI Platform, Permissions and Human-in-the-Loop Governance](https://github.com/nickmailhot-lang/StrataAI2/issues/73) | open | PRD-24, PRD-35, PRD-53, PRD-70 |
| PRD-73 | [[PRD-73] AI Email Intake, Triage, Card Routing and Response Drafting](https://github.com/nickmailhot-lang/StrataAI2/issues/74) | open | PRD-34, PRD-35, PRD-65, PRD-72, PRD-77, PRD-78, PRD-79 |
| PRD-74 | [[PRD-74] AI Meeting, Agenda and Minutes Assistant](https://github.com/nickmailhot-lang/StrataAI2/issues/75) | open | PRD-37, PRD-39, PRD-40, PRD-41, PRD-72, PRD-77 |
| PRD-75 | [[PRD-75] AI Search, Question Answering and Source Citation](https://github.com/nickmailhot-lang/StrataAI2/issues/76) | open | PRD-35, PRD-50, PRD-56, PRD-72 |
| PRD-76 | [[PRD-76] AI Document Intelligence](https://github.com/nickmailhot-lang/StrataAI2/issues/77) | open | PRD-48, PRD-49, PRD-72 |
| PRD-77 | [[PRD-77] Approval Workflows, Review and Sign-Off](https://github.com/nickmailhot-lang/StrataAI2/issues/78) | open | PRD-24, PRD-53, PRD-65, PRD-69, PRD-72 |
| PRD-78 | [[PRD-78] Duplicate Detection, Card Merge, Split and Reconciliation](https://github.com/nickmailhot-lang/StrataAI2/issues/79) | open | PRD-08, PRD-41, PRD-53, PRD-57, PRD-73 |
| PRD-79 | [[PRD-79] Shared Mailbox, Outbound Email and Correspondence Delivery](https://github.com/nickmailhot-lang/StrataAI2/issues/80) | open | PRD-65, PRD-72, PRD-73, PRD-77 |
| PRD-80 | [[PRD-80] Owner and Resident Portal](https://github.com/nickmailhot-lang/StrataAI2/issues/81) | open | PRD-24, PRD-29, PRD-48, PRD-51, PRD-58, PRD-60, PRD-70 |

## Verification and closure discipline

For each ticket, derive its functional requirements, acceptance criteria, linked test cases and definition of done from the current issue body before implementing or closing it. Match evidence to each requirement; local compilation, source tests, image fixtures and browser execution prove different scopes. A passing producer test does not prove its downstream consumer, privacy/access boundary, accessibility, performance or full lifecycle behavior.

Latest repair a074f991a0c082ce43384e55dfcc2dacaa8df4ba restores locked Board snapshot admission under archived Organizations. Its .NET build and fixture syntax checks passed locally; exact-image CI run 37054915157 is pending. Previous run 37052462546 passed the Card assignment/options/previews/member-filter fixture and subsequently failed the archived-Organization snapshot assertion. No full-current-main green gate or remaining-ticket completion is claimed.


## PRD-15 protected activity query milestone

Authenticated internal Board and Card GET activity endpoints now return at most
50 body-free items with an opaque 15-minute continuation bound to Organization,
viewer, target kind and stable target ID. Source captions remain historical;
entity versions are decimal strings. Responses prohibit caching. Card queries
include eligible historical source Boards and personal Card Watch/Reminder
sources. SQL and Demo filter current source/target visibility and personal
ownership before the 51-row candidate limit. The Application discovers the
complete source/current Board lock set before acquiring canonically ordered
Board gates, then repeats parent/role admission and uses the existing issuing
session transaction protocol. Invalid cursors are disclosed only after root
admission. Private Board watch targets must retain their original source Board.

Migration 065 adds ordered global Card and private-reference history indexes;
readiness and migration rollback/repeat fixtures require all 65 migrations.
Demo journal projection normalizes timestamps to PostgreSQL microsecond UTC
precision while retaining original exact Work event retry identity.

Validation: warning-as-error solution build passes locally. New mandatory API
host fixtures cover tied pagination, hidden private history before the limit,
viewer/target cursor binding, malformed cursors and revoked access; restricted
PostgreSQL fixtures execute the production Board candidate query and seek.
Linux CI executes these tests because Windows Application Control prevents
local managed test execution. These are not full acceptance: historical Card
SQL/private-owner stress, exact-image lock waits/session expiry, MUI rendering,
realtime refresh, accessibility/mobile, capacity, retention and shared persistent
Data Protection key deployment still require validation and implementation.
PRD-15 remains open, estimated 50% remaining until those criteria are proven.

The activity views now render in the existing MUI Board and Card screens on
explicit review. Each page holds at most 50 DOM rows, with older/newer/newest
navigation across the full history. The parser validates source/current tenant
and target identities, safe body-free fields, exact microsecond order and string
bigint versions. Captured captions render as literal text and Card links use the
current Board. Reads verify `/me` before and after the feed; parent read epochs,
realtime invalidations and reconnects retire prior pages/cursors, abort old reads
and exclude delayed replies. Access loss purges protected state. Loading, empty,
error/expired-cursor retry and keyboard focus recovery are provided. This is
component-level evidence pending native desktop/mobile/two-client CI acceptance.

Production/release Compose now mounts a dedicated API-only named volume at
`/var/lib/strataai/activity-keys`, using `STRATAAI_ACTIVITY_KEY_DIRECTORY` and
fixed `StrataAI2.InternalActivity` Data Protection application isolation. The API
image creates its key directory with mode 0700; the Worker/web services do not
mount it. Preserve that volume during upgrades; deleting it intentionally
invalidates existing history continuations (clients recover from newest history).
Operators running APIs outside Compose must configure an absolute protected
key directory; replicas must share the same application name and key directory.
The mounted filesystem should use the deployment's encrypted storage and backup
controls. No key material belongs in web roots, CI artifacts or release bundles.
A mandatory managed test opens two independently constructed providers on the
same persisted directory and rejects a third unrelated application domain;
exact-image restart/replica execution still requires release verification.

Required immutable-image CI now runs `test-activity-feeds.sh` after comment
mention checks. It uses real sessions, current entity parents and restricted API
transactions to verify complete tied history pages, cursor target/viewer binding,
API container recreation with preserved continuation keys, immutable actor
captions after rename, observed source/current Board lock waits with membership
revocation, an observed account lock wait with issuing-session revocation and
natural session expiry across a late Board wait. Historical source birth is an
explicit SQL fixture, not an implemented cross-Board Card movement claim. It
retains the existing isolated auth fixture configuration across recreation and
waits for the edge to resolve the replacement API. Shell syntax passes locally;
actual PostgreSQL/image execution occurs only in the mandatory Linux release
pipeline. These scenarios are not claimed passed until their exact-image step
finishes successfully.

Required native activity browser coverage now defines separate 1280px and 390px
scenarios. Real distinct accounts, Organization invitations and Board membership
create 65 actual Card mutations before history review. Keyboard paging checks
50/16 Card pages, focus recovery, literal captured captions and absence of body
content. Two independently authenticated clients recover comment activity;
the reader's real Board WebSocket is explicitly disconnected and reconnects
after a missed, Worker-delivered event. Revoked membership removes displayed
history and refuses its previous cursor. Owner archive/delete commands preserve
permitted API and Board activity tombstones. Axe WCAG 2.2 AA and viewport overflow
checks run against the real MUI interface. Playwright discovery passes locally;
actual execution requires the immutable-image release topology and is not yet
claimed passed. This does not substitute source/component tests for native UI,
Worker or SignalR evidence.

Activity capacity validation now reuses the real supported-size Checklist parent
fixture (200 Lists, 5,000 active Cards and 100,000 archived Cards), adds 100,000
synthetic immutable Card history sources, and exercises Board/Card first and
seek pages through the restricted API/Nginx. It verifies 50-row pages, 100 distinct
IDs across two pages, body-free items and unchanged read state, and measures 20
requests per endpoint. A dedicated retained artifact contains only fixed fixture
sizes, verification flags, numeric samples/p95s and the immutable revision. No
source identities, captions, bodies, profiles, cursor tokens or SQL are retained.
Shell syntax passes locally; actual execution is a required release step. This
setup measures API read capacity; it does not claim actual audited history birth,
a browser rendering budget, exhaustive 100,000-event enumeration or a retention
policy that silently discards old events.

Internal activity retains its append-only, body-free history without an automatic
age cutoff. The API runtime has SELECT/INSERT journal permissions; the Worker
can only read delivery fields and update `ready_at`. There is no Work journal
purge job or DELETE grant. Soft entity/account lifecycle preserves historical
identities, and every read still requires current source/target visibility and
personal ownership. A cursor expires after 15 minutes; it does not expire the
underlying history. A new session and freshly admitted continuation can page old
sources. A managed HTTP scenario advances a controlled clock five years after
66 real Card history mutations, verifies the old session and cursor are refused,
then uses fresh authentication to read all 67 old/current events. This is Demo
HTTP/clock evidence passed in Linux CI for `2b49be4` (run 37188015571), not a real five-year deployment
or a new configurable retention/erasure policy. Cross-PRD data-retention changes
must preserve explicitly approved activity/audit rules rather than silently
purging immutable sources.

That revision's managed, PostgreSQL and web source jobs all passed; immutable
image build and full release checks remain separate gates. The `13865bf` run
37187388271 passed the exact-image unsafe-role/ledger step and activity step 37:
complete tied cursor seek, binding refusal, persisted-key API restart recovery,
immutable actor captions, historical/current Board post-wait revocation,
issuing-session revocation and natural session expiry during a live Board gate
wait. Its full container/native browser gate remains pending. This establishes
the scoped activity fixture, not full PRD acceptance or actual Card movement.

The MUI activity reader now treats 401/403/404 as a terminal denial for the
current parent access generation. It discards rows and continuation, stops
protected requests, hides retry and disables refresh/newest controls until a
fresh Board access generation arrives. Previously resetting pagination on a
denial could launch another read before the parent cleared its view. Keyboard
focus moves to the available close action. Three component scenarios exercise
each denial after a valid older-page continuation, requiring exactly one denial,
no automatic retry, cleared protected rows and a cursor-free read only after
fresh parent admission. This does not replace real post-wait revocation or
native browser execution. PRD-15 remains open with approximately **45%** remaining.

Activity opening/read/retry/exception observations now have a bounded MUI queue,
an authenticated fixed-category endpoint sharing the Checklist abuse budget,
native aggregate instruments and the existing optional OTLP export path. See
[activity history telemetry](architecture/activity-history-telemetry.md) for the
protocol, privacy boundary, source tests and mandatory Collector proof. Local
typecheck/lint/build, 19 focused activity/Checklist transport/component tests,
three operator-validator tests and zero-warning managed compilation pass.
Linux managed/exporter and actual release Collector execution remain pending.

Comments and mentions now use that fixed observation path for disclosure/read,
create/edit/delete attempts and recovered original receipts, definite conflicts,
client exceptions, clean-view reconnect recovery, teammate lookup/selection and
explicit group confirmation. No body, username/prefix, selected identity or
recipient list becomes an observation. Local comment/group/teammate/recovery
regressions and activity/transport checks pass (33 tests), including safe fixed
reports; three operator-validator tests and managed compilation also pass.
The activity denial focus ordering fix passes its 11 focused scenarios with a
synchronous parent access publication. New managed and release Collector
execution remains required. Earlier `6574058` has passed web, PostgreSQL and
managed source jobs in run 37188968317; its complete release gate remains live.

Comment read capacity now has its own mandatory release fixture and retained
fixed-scope measurements. It reuses the supported Board/archive dataset, adds
100,000 guarded synthetic comments, verifies unique 50-row cursor pages and a
final body-redacted tombstone, no-store headers and unchanged full comment/Card
state and publication counts, then measures 20 first-page requests. See
[comment capacity](architecture/comment-capacity.md) for scope and limitations.
Shell syntax passes locally; actual release execution is pending. This does not
claim synthetic setup as audited mutation history or a browser timing budget.

The comment capacity fixture now separately measures twenty real serial comment
creates through the authenticated immutable API/Nginx path after its read-only
boundary. It requires distinct replies, exact Card revision and comment/event/
audit/snapshot/receipt count advances, original acknowledgment recovery without
duplicate effects and stable old-cursor version refusal. Complete HTTP mutation
acknowledgments must meet the PRD p95 <500 ms target under the documented single
serial-client/no-intentional-latency condition. Only fixed verification flags and
numeric timings reach the retained artifact. Shell syntax passes; actual runtime
execution and timing proof remain pending.

The [PRD-15 acceptance map](architecture/prd-15-acceptance.md) now records every
functional requirement, numbered acceptance criterion/linked scenario, current
evidence scope and remaining producer/runtime gap. It explicitly retains actual
cross-Board movement (PRD-08) and archived Card/List detail controls (PRD-09/18),
which API/synthetic history checks cannot complete. Current code inspection
confirms those gaps; neither is waived by the audit.

FR-010 now has an actual HTTP scenario: a participating non-owner teammate
authors a Card mutation, renames the profile and legally deactivates the account,
then a current owner reads the identical captured event/caption in both Board
and Card history while the former session is refused. Native desktop/mobile
activity scenarios now include a real teammate mutation, renamed/deactivated
account and original caption in the older Board page after permitted tombstone
lifecycle. Compilation/discovery is separate from Linux/runtime/browser execution,
which remains pending for these new cases. The optional activity Collector gate
at `6574058` has passed exact-image steps 42–44 in run 37188968317; comment
observation/capacity/runtime claims remain separate requirements.

### Archived Card detail and verified comment capacity (2026-10-04)

The active canvas omission now leads to a separately admitted internal archived
Card detail read: archived Cards and Cards in archived Lists expose read-only
content, comments and activity. Current Organization/Board access and fresh
parents are checked through the existing read transaction and Board gate;
deleted content remains unavailable. The archived Card directory links to detail.
Local focused UI/recovery tests and the full .NET warning-as-error build pass;
actual API and desktop/mobile release scenarios await the next exact CI run.

Run 37190533527 on 4ab794d passed exact-image comment capacity and the extended
activity/comment operator-metric privacy verifier. Inspected retained evidence
reports first-page p95 34.345 ms and real serial comment-command p95 57.554 ms at
100,000 comments with supported Board/archive sizes, atomic effects, unchanged
read/retry state and refused stale cursor. Conditions and limits are documented
in docs/architecture/comment-capacity.md. Native/runtime completion and actual
cross-Board movement remain outstanding; PRD-15 stays open, about 40% remaining.

### Cross-Board historical notification integrity (2026-10-04)

PRD-08 movement requires the historical notification storage dependency in
migration 066. Notifications now keep original source Board/event attribution
while referencing the stable same-tenant Card, plus an exact source-event/Card
identity constraint. Populated repeat-upgrade and restricted raw storage movement
cases preserve complete envelopes and reject unrelated same-tenant subject
rebinding. Runtime schema and missing-migration/restore evidence require 66
migrations; synthetic runner fixtures advance to 067–069. Compilation and Bash
syntax pass locally; actual new SQL/upgrade/runtime evidence awaits Linux CI.
The full command, Board-scoped references, notification projection and native
consumer acceptance remain required; docs/architecture/cross-board-card-movement.md
records this dependency without claiming the API movement feature complete.
