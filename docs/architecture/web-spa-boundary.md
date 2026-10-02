# Web SPA routing and state boundary

ARCH-02's routing choice is React Router's browser router, created once by
`apps/web/src/app/App.tsx`. The shipped application is a React/TypeScript/Vite
SPA using MUI's theme and components and dnd-kit for Kanban interaction.
Nginx serves compiled assets with SPA fallback and proxies `/api/` to the private
`api:8080` service. Existing same-origin domain and realtime routes also proxy
through the edge. No additional local development service is required by this
decision.

## Query and cache decision

Current features use typed API services, the shared `apiFetch` transport and
feature-owned React state. There is no global query-cache library. This records
the implemented choice; it does not waive any architecture requirement.

Board state is keyed by Organization and Board. Switching either remounts the
feature and cancels the previous read and subscription. Card details reuse that
authorized snapshot. Reads, pending mutation intents, dirty drafts and validated
acknowledgments have separate lifetimes: a live refresh must neither discard a
dirty draft nor erase a retry needed after an uncertain mutation result. Access
loss clears protected state and fences late responses. Server receipts and
current authorization remain decisive. See [Board interface](board-interface.md)
and [work synchronization](work-synchronization.md).

Browser storage is not the domain system of record. Invitation creation retains
a session-scoped retry intent, documented in
[invitation administration](invitation-administration-ui.md); the server remains
authoritative for invitations, permissions and outcomes. Do not persist Board
snapshots or authentication secrets to implement this cache choice.

Shared transport and Problem normalization live in `src/api`; shared shells and
admission controls live in `src/app`, with MUI configuration in `src/theme`.
Implemented domains live under `src/features`; the separate Owner Portal shell
lives under `src/portal`. Future domains must retain feature ownership rather
than expanding a global domain store. Portal and internal route admission are
independent, and API authorization independently enforces their boundaries.

## Requirement evidence and remaining scope

| ARCH-02 boundary | Executable evidence |
| --- | --- |
| Deep-link SPA resolution, AC-001 | CI exact-image deep-link smoke step; authenticated Board and surface browser scenarios |
| Edge API health proxy, AC-002 | `scripts/health-check.sh`, run against the exact release topology |
| Portal isolation, AC-003 | `SurfaceAdmission.test.tsx`, `tests/browser/surface-admission.spec.ts`, independent API rejection assertions |
| Matching build identity, AC-004 | `buildIdentity.test.ts`, `scripts/ci/test-build-identity.sh`, `tests/browser/ui-build-identity.spec.ts` |
| Shared safe error handling, FR-009 | `apiFetch.test.ts`, `apiProblem.test.ts`, CI feature HTTP boundary check |
| Component and workflow tests, FR-010 | Vitest/Testing Library source gate and Playwright exact-image gate |

The full CI run for `772590dc03aef91f92e09d54fc942bd1f2b830ee`
([36983124293](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36983124293))
passed all release gates, including these architecture checks. That evidence
predates subsequent main changes and does not establish a green current release.
At `7e88e21`, 426 web tests, typecheck, lint and production build passed locally;
exact-image CI remains pending. This document does not close ARCH-02: its full
dependency, functional, accessibility, observability and definition-of-done audit
still must be satisfied. Unimplemented product domains are not represented as
completed merely because their module convention is defined here.
