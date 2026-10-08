# Web SPA routing and state boundary

ARCH-02's routing choice is React Router's browser router, created once by
`apps/web/src/app/App.tsx`. The shipped application is a React/TypeScript/Vite
SPA using MUI's theme and components and dnd-kit for Kanban interaction.
Nginx serves compiled assets with SPA fallback and proxies `/api/` to the private
`api:8080` service. Existing same-origin domain and realtime routes also proxy
through the edge. No additional local development service is required by this
decision.

Vite's local proxy also forwards top-level API routes when the request URL has
a query directly after the route name, such as `/search?q=deadline`. Its route
boundary matches slash, query marker or end of URL; SPA paths remain outside the
listed API roots. The previous slash/end-only expression returned the SPA HTML
with HTTP 200 for queried root endpoints. Actual desktop and phone search
fixtures reproduced that failure, then passed after the query-boundary repair.
Nginx already matches the request path separately from its query and needs no
change. This local proxy result does not certify all current release routing.

## Query and cache decision

Surface admission retains an already admitted feature tree while a transient
access read is unavailable, but hides its entire surface from display and the
accessibility tree. Fresh successful admission reveals that same tree, preserving
original uncertain-command state and validated live cursors. Current denial
unmounts protected content; a different Organization/surface cannot inherit the
retained state. Initial admission withholds content; explicit transport retry
preserves the hidden recovery tree until successful fresh admission. MUI
Modal/Popover/Popper portals use the mounted surface container, and hidden dialogs
do not enforce focus, keeping access retry reachable. Children mount only after
the container exists so their first portal effect cannot fall back to the body.
Every feature API independently authorizes reads and commands. The regression
proves transient recovery retains component state while denial destroys it; the
complete local desktop/phone [notification scenario](notification-inbox.md#current-local-preference-and-reconnect-evidence)
proves actual retained-cursor replay after offline recovery. An installed-Dialog
regression verifies hidden content, reachable retry and unchanged unsent draft.
Both complete desktop/phone comment scenarios also pass the final shell boundary.
This repairs a native
failure rather than substituting HTTP recovery for the required live replay.

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

## Shared MUI input styles

The root `App` retains one `CssBaseline` under the shared theme. Its theme
overrides own MUI's non-empty `mui-auto-fill` and `mui-auto-fill-cancel`
keyframes, including Emotion's standard/WebKit variants. InputBase disables
per-field global-style injection; the theme input override preserves the normal
10 ms cancellation animation and the WebKit autofill selector's 5000 s detection
animation. This avoids adding/removing duplicate keyframe sheets as forms and
Card details mount, without changing input values or feature admission.

`appTheme.test.tsx` verifies stable shared rules across zero, five, zero and two
fields in StrictMode, and filled-label handling for autofill/cancellation
notifications. Both desktop/phone `input-autofill.spec.ts` cases pass locally
against the frozen production web bundle through Nginx. They check actual CSS
rules/computed cancellation styles, the autofill selector, synthetic animation
notifications, label association and form navigation without duplicate rules.
These synthetic notifications exercise MUI's listener, not an actual browser
password manager. The 59 selected theme/shell/authentication/recovery/editor
component tests, web/browser TypeScript, targeted lint and production build
pass. Current immutable-image execution and complete ARCH-02 acceptance remain
required. See [Kanban timing evidence](../kanban-performance.md) for the unchanged
timing requirements and their measured outcomes.

## Routed render-error boundary

Top-level routes inherit a fixed MUI error view. It displays no diagnostic Error
data and offers explicit reload with an unsaved-draft warning; it does not
automatically retry a command. Production React caught-error logging is reduced
to a fixed message, while development keeps normal diagnostics. The router's
render-error callback emits only a fixed aggregate category through the existing
bounded activity telemetry path. Loader/action errors do not become render
counts. See [caught render telemetry](../kanban-telemetry.md#caught-routed-render-failures)
for scope, privacy, evidence and outstanding crash-recovery requirements.
This boundary does not recover state destroyed with a failed feature tree and
does not complete the architecture's error/recovery or release audit.

Production startup also installs private-free browser exception counters outside
StrictMode. Root uncaught/recoverable callbacks use fixed diagnostics and separate
aggregate categories; routed caught errors retain their existing owner. Global
script/event and unhandled-promise observers suppress default private diagnostics
while preserving feature state and other listeners. The same bounded collector
requires exception-only categories with no Error, reason, URL, identity or timing
fields. Development keeps default diagnostics. See
[runtime exception coverage](../kanban-telemetry.md#runtime-exception-coverage)
for executed source/runtime evidence and remaining recovery/observability limits.
The production root now supplies a document-primitive emergency view after an
uncaught failure removes the React tree. This fallback must work without the
failed React/router/theme renderer; MUI remains the primary UI and routed-error
fallback. Fixed recovery text warns about possibly completed commands and draft
loss, focuses a heading and offers one explicit keyboard-accessible reload.
Recoverable root errors leave the live view intact. See
[fatal root recovery](../kanban-telemetry.md#fatal-root-recovery-view) for the
implementation, regression evidence and limits. Destroyed drafts, pre-root
startup failures, anonymous collection and the current immutable-release audit
remain outstanding.

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


## Contrast during disabled-to-enabled control transitions

The shared MUI theme preserves floating-label movement and shadow/border motion
while applying interactive label and button colors immediately. This prevents
brief unreadable colors when an acknowledged registration re-enables its form.
The palette, disabled styling, focus/ripple and command guards are unchanged.
A rendered transition regression fails before the fix; all thirteen theme/auth
source cases, typechecking, targeted lint and production build pass afterward.
Both unchanged strict-policy desktop/phone Worker-backed verification/recovery
cases pass together, including twenty Axe/overflow checks. See
[executed contrast repair](identity-email.md#enabled-control-contrast-after-registration)
for the terminal immutable failure, measured contrast, real local mail evidence
and limits. Current immutable release/full architecture acceptance remains
required; estimated ARCH-02 work remaining stays **39%** (planning estimate).

## Current surface admission and watch keyboard execution

The initial three-case native invocation passed the unchanged watch scenario but
failed both surface scenarios because the fixture expected one named navigation
element where the admitted layout intentionally has two (nav and nested list).
The fixture now requires both; all absence assertions and protected API checks
are retained. Organization Home separately uses neutral Organization denial on
403/404, covered by two failing-before/passing-after component regressions.

The held-read watch regression also fails before the fix: completion moves focus
away from an enabled Done button that the user deliberately focused. The fix
preserves that dismissal choice while retaining unknown-command Retry recovery.
All 86 related component cases, web/browser typechecks, targeted lint and the
production build pass; the existing bundle-size advisory remains.

The final Production native invocation passes all three cases together in
2.7 minutes: desktop/phone Portal isolation, membership removal, independent API
denial, and the unchanged desktop/phone Board/List/Card watch workflow with a real
scoped Worker, cross-client recovery, committed lost-response same-key/body retry,
Card movement and archived-parent denial. No watch browser assertions changed.
This is compiled Nginx/API/restricted PostgreSQL execution, not proof of a green
current immutable release. Full ARCH-02 acceptance remains pending (**39%** work
remaining, planning estimate). See [Organization denial](organization-discovery.md#organization-home-denial-after-independent-admission)
and [watch dismissal focus](watch-subscriptions.md#preserve-dismissal-focus-during-a-protected-read).

## Owned MUI trap-container recovery

The shared focus helper recognizes the installed MUI dialog's exact ancestor
trap container as owned fallback, in addition to its marked paper. It continues
to exclude another control or another dialog. An installed-MUI regression fails
before the repair; all 170 selected cases across ten consumer suites pass after
it. Web/browser typechecks, targeted lint and production build pass, retaining
the existing bundle-size advisory.

Actual label browser execution still fails. Fixture repairs now distinguish a
visible popup listbox from its identically named or accessibility-hidden
combobox, observe read-only picker opening, require enabled mutation controls,
and explicitly verify deletion consent before the single command. Subsequent
runs expose additional return-focus, Clear activation and cross-client assignee
gaps; none is represented as passing acceptance. See
[label execution and remaining failures](../board-label-api.md#mui-trap-fallback-and-keyboard-menu-repair).
Current immutable CI/full architecture acceptance remains required; ARCH-02 stays
open at **39%** estimated remaining work (planning estimate).
