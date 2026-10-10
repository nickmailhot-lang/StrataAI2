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

This improves FOUND-FR-008 coverage for the named routes and long content.
It does not certify every primary-screen state, Board virtualization/geometry,
all permission roles, physical devices, WCAG acceptance or current build-once
CI. The original [account phase](profile-error-references.md) passes separately
20/20; [Board/Portal evidence](prd-01-acceptance.md) retains its own source and
runtime boundaries. Full foundation acceptance remains open at **34% estimated
work remaining** (planning estimate).
