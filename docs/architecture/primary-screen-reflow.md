# Primary-screen reflow with long canonical names

The new [four-width browser cases](../../tests/browser/primary-screen-reflow.spec.ts)
use actual registration, login and Organization creation to exercise supported
100-character unbroken User/Organization names and a long valid account address.
They cover authentication/recovery forms, Organization listing and home, profile
and safe deactivation cancellation, Organization settings, notifications, search
and recipient invitations at **1280, 768, 390 and 320 CSS pixels**. They require
complete labels/field values, visible ready controls and document width no larger
than the viewport. The final read confirms the account was not deactivated.

## Observed defect and repair

The complete baseline fails **0/4**: every width reaches the profile and then
measures a **3,442-pixel** document at the unbroken profile heading. Earlier
authentication/recovery/list checks pass, but later screens are not executed
proof in that baseline. This is a valid persisted label, not a malformed-input
fixture. Reports and screenshots remain outside source.

[The MUI theme](../../apps/web/src/theme/appTheme.ts) now allows Typography to
wrap long words at available width. Full names and addresses remain present;
there is no clipping, ellipsis, overflow hiding, smaller fixture or weakened
viewport assertion. The new invitation-heading locator is corrected to the
actual `Your invitations` screen before repaired verification; that heading
was not reached in the failed baseline.

The complete repaired native phase passes **4/4 on their only attempt**, with
zero skipped, flaky, unexpected cases or report errors, in 132,710.107 ms.
The current profile frontend plus wrapping repair runs with compiled schema-133
restricted API/Worker/PostgreSQL and Nginx/CSP. This phase uses the original
optional-verification policy, separate from strict mail/token acceptance. All
four 90-second case budgets and 25-second release-fixture pacing remain.
Baseline and repaired databases, API/web/Worker containers and credential
environments are independently absent after terminal cleanup.

Private reports: `primary-screen-reflow-before-native-20261010` and
`primary-screen-reflow-after-native-20261010`.

## Source and release boundaries

Web/browser typechecks, lint and the current private build pass. Actual browser
collection retains **327 cases in 125 intact files**, 83/81/99/64 across four
shards. The four new cases are added to the full release collection; original
cases, scopes, deadlines and no-retry configuration remain unchanged.

The first selected authentication/API/theme source run records **320/321**,
with an existing mention-handle test exceeding its unchanged five-second
deadline. A second complete same-pool invocation records **318/321**, with
three existing five-second timeouts (registration, deactivation confirmation
and mention-handle recovery), not behavior-assertion failures. Compiler checks
overlap the first invocation; overlap is not established as the sole cause.
Both reports remain retained. A complete same-scope invocation with two file
workers passes **321/321 across 22 files**, zero failed/pending. Per-case
deadlines, within-case concurrency, assertions and case counts remain unchanged,
and no case retry option is added. This
local execution topology does not replace default-pool or immutable CI proof.

The subsequent **complete frontend source suite passes 2,037/2,037 across 142
files**, zero failed or pending, using two file workers. It has no case/file
filters or added retry option and preserves the original per-case deadlines and
within-case concurrency. Its private terminal report is
`frontend-full-wrap-source-20261010/full-report.json`; the earlier default-pool
timeouts above remain retained. This is local source proof, separate from the
queued exact-head immutable-image CI.

This improves FOUND-FR-008 coverage for the named routes and long content.
It does not certify every primary-screen state, Board virtualization/geometry,
all permission roles, physical devices, WCAG acceptance or current build-once
CI. The original [account phase](profile-error-references.md) passes separately
20/20; [Board/Portal evidence](prd-01-acceptance.md) retains its own source and
runtime boundaries. Full foundation acceptance remains open at **34% estimated
work remaining** (planning estimate).

## Complete current Board regression phase

The original fourteen-file Board phase on the wrapping frontend passes
**32/32 on their only attempts**, zero skipped/flaky/unexpected cases or report
errors, in 1,485,070.052 ms. It uses compiled schema-133 API/Worker, fresh
restricted PostgreSQL with all migrations through 133, Nginx/CSP and strict
email verification. The unchanged disposable-account fixture checks first
login refusal before activating only its own accounts. Original scopes,
assertions, deadlines, pacing and no-retry settings remain.

Private report: `board-schema133-wrap-full-native-20261010/report-private.json`.
The owned API/web/Worker containers, database and credential environments are
independently absent after terminal cleanup. This whole-phase regression is
separate from four-width primary-screen geometry and exact-image CI; it does
not establish every Board permission/performance requirement.


## Board and Card long-label reflow

The added [four-width Board/Card cases](../../tests/browser/board-screen-reflow.spec.ts)
exercise real private Boards with 100-character unbroken Board, List and Card
labels at 1280/768/390/320 CSS pixels. They verify complete Card-preview text,
intrinsic preview/drop-target/column/action widths, four enabled List/Card
controls, document and open-dialog widths, keyboard Board edit/List rename
cancellation, real Card deep-link opening/closing and restored canvas focus.
The final protected Board read verifies the original labels and entity IDs.
Intentional horizontal canvas scrolling remains supported.

The initial native phase fails **0/4** on Card-preview intrinsic overflow;
complete text and document width checks before it pass, while subsequent actions
are not reached. The 768-pixel result also retains two diagnostics without source
locations; their cause is unclassified. CardContent now wraps plain canonical
titles, and MUI Button labels wrap without changing their existing transitions.
The next phase records **3/4**: the desktop Board-edit field is absent after the
keyboard action, after the geometry checks passed. The fixture then waits for
both protected Board reads and the workspace's admitted non-busy state before
acting. The following phase passes **4/4**, but predates the stronger drop-target
and whole-column guards and does not prove those guards.

With those final guards, all four widths fail again on the List-end drop-target's
unbroken label. The actual drop Box now wraps complete text. The final native
phase passes **4/4 on their only attempts**, zero skipped/flaky/unexpected cases
or report errors, in **148,235.747 ms**. It uses compiled schema-133 Production
API/separate Worker, fresh restricted PostgreSQL, strict email verification and
Nginx/CSP. Original 90-second case budgets, 25-second pacing and no-retry settings
remain. Independently checked terminal cleanup finds zero owned containers,
databases or credential environments for the admitted and both column phases.
All intermediate private reports remain separate:
`board-screen-reflow-before-native-20261010`,
`board-screen-reflow-after-native-20261010`,
`board-screen-reflow-admitted-native-20261010`,
`board-column-reflow-before-native-20261010` and
`board-column-reflow-after-native-20261010`.

The first complete source invocation on the CardContent/Button repair records
**2,034/2,037**, with three existing five-second test deadline failures. The
report is retained at `frontend-full-board-wrap-source-20261010/full-report.json`;
concurrent compilation/API/browser workloads are not established as the sole
cause. The complete final source invocation including the drop-target repair
passes **2,037/2,037 actual assertions across 142 files**, zero failed/pending,
with the same two file workers, original deadlines, within-case concurrency,
unfiltered scope and no retry options. Its report is
`frontend-full-board-column-wrap-source-20261010/full-report.json`.
Final web/browser typechecks, lint, build and whitespace checks pass. Browser
collection contains **331 cases in 126 intact files**, 83/83/83/82 across four
shards, retaining original cases and single-file ownership.

These results cover the named Board/Card states and four CSS viewports. They do
not replace full permission, accessibility, physical-device, performance or
current immutable-image CI acceptance. The previous 32-case Board phase above
used the earlier Typography-only frontend; it is not relabeled as this final
frontend's complete regression. PRD-01 remains open at **34% estimated work
remaining** and PRD-04 at **15%** (planning estimates).
