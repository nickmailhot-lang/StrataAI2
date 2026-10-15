# Label keyboard admission verification

This supports the [operational MVP milestone](operational-mvp-milestone.md) and PRD-10. It does not establish release acceptance.

## Hosted evidence and change

The retained artifact for [CI run 38287805799](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38287805799) identifies commit `1d03b0c9ec56d50c336f319099fe37922a174739`. Its mobile Card-label scenario fails at the consent checked assertion. The trace shows the checkbox enabled immediately before Space and disabled during the keypress; the following checked assertion fails. This is evidence of admission withdrawal during activation, rather than evidence that deletion consent was accepted.

The scenario now observes the actual creation name field after the read-only Create label opening. For removal consent it observes the local checked state within the existing five-second admission budget, activates Space only while unchecked, and stops once checked. This does not send a deletion request. The separate deletion activation still counts exactly one DELETE. Original creation and assignment recovery keys/bodies, move count, persisted label versions/order, removal associations and 90-second case deadlines remain intact.

## Local verification, October 12, 2026

- Unchanged scenario: two passed, zero skipped/flaky/unexpected, 122,215.886 ms. The hosted interruption did not reproduce in this ordinary local baseline.
- Changed scenario: two passed, zero skipped/flaky/unexpected, 122,531.594 ms.
- Label creation/management component checks: 38 passed, zero failed/pending. Browser types, solution build and 213 CI guard checks pass.
- Both captures use restricted PostgreSQL/RLS, compiled Production API, separate Worker, Nginx and verified disposable fixture accounts. Original case deadlines and pacing remain unchanged.
- Independent final checks: 2,089 hosted source files and 1,414 native source files match Primary, as do all copied web assets. Owned containers, database, roles and credential files were removed; the original stopped PostgreSQL and its volume remain intact.

Private reports remain outside the repository under `ci-label-keyboard-hosted-live1/live2-20261012` and `ci-label-keyboard-native-browser-live1/live2-20261012`. These mounted local builds are supporting evidence, not certification of the immutable release images. Full current CI, retained-image verification and all original PRD acceptance remain pending. Estimated PRD-10 work remaining: **35%**, unchanged.

See [Board label contracts](../board-label-api.md) and the [documentation index](../README.md).

## Live filter assignee chooser follow-up

The retained notification-browser artifact for [CI run 38345821127](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38345821127) identifies commit `a89628f8cc2fecf39290fcd90be9c8742c1903db`. Artifact `11791549221` has SHA-256 `a0ce4846febf8cf2719a8557a5cf7494f94a8ae3f300d65c2bc7b6392645451b`. Reconstructed trace snapshots show **Choose assignees** enabled before Enter, disabled during the keypress and enabled again afterward. Its label remains **Choose assignees**, and the expected member checkbox never appears. The original check fails at `label-filter-live.spec.ts:173`.

The scenario now uses the existing bounded read-only opening helper and observes the actual member checkbox. This helper retries only the chooser opening within the original five-second budget. Checkbox selection and Apply filters still use their original single activation. The original four filter changes, live assignment withdrawal/restoration, archived-Board withdrawal, HTTP refusal, viewport checks and 180-second scenario deadline remain unchanged.

The unchanged local baseline passes all three complete Board metadata and live-filter cases in 195,883.930 ms; the revised ordinary capture passes the same three in 191,751.187 ms. Neither capture skips or retries a failed case. Browser types, a fresh solution build and all 213 immutable CI guards pass. Independent checks for both captures find zero mismatches across 2,109 hosted source/runtime files, 1,414 native source files and four web assets. Each capture uses restricted PostgreSQL/RLS, the compiled Production API, separate Worker and verified disposable fixture accounts. Owned containers, database/roles and temporary credentials were removed; the original stopped PostgreSQL volume was preserved.

Two additional private controlled probes are excluded from regression proof: an unsynchronized refresh passes the old opening, while a refresh scheduled before the original press reaches the original scenario timeout without proving the hosted failure. Neither establishes a before/after result. The hosted phone Board metadata focus failure was unresolved at this capture; [its later owned-focus follow-up](../board-metadata-control.md#owned-focus-after-protected-reads-2026-10-14) records supporting repair evidence. Mounted local runtime evidence does not certify the immutable CI images; complete current CI and the original acceptance requirements remain necessary. Estimated PRD-10 work remaining stays **35%**; the issue remains open.

## Label-management refusal-reference opening follow-up (2026-10-14)

Retained run `38505687589`, shard 2 job `115689442307`, fails phone label
editing while waiting for an absent Edit control. Its trace shows Manage-labels
focus/Enter, then no browser label read. The missing read places failure before
the intended refused edit, rather than proving loss of the server reference.
Current unchanged local read/edit desktop/phone cases pass **4/4 in
180,653.881 ms**; no hosted-failure reproduction or product root cause is claimed.

The fixture now observes the actual management dialog and edit field using the
existing bounded read-only opening helper. Save activation stops when its one
intended PATCH is observed. Actual admitted competing edit, real 400/409 refusal,
one deliberate failure dispatch, returned 64-character correlation reference,
canonical unchanged content, reference retirement after reload, viewport,
accessibility and the 90-second deadlines remain unchanged.

The complete changed file passes **4/4 in 179,620.583 ms**, zero skips,
unexpected/flaky results, retries, report errors or diagnostic hooks. Browser
types, 224 CI/shard/startup guards and all 398 cases across intact shards pass;
collection is not execution. Independent checks match 2,143 hosted inputs,
1,441 native inputs, 168 frozen browser files and four served assets. Owned
fixtures, credentials and newly pulled support images are removed; original
stopped PostgreSQL is preserved. These compiled-runtime/controlled-account
results do not certify strict managed mail or current immutable/full release
acceptance. PRD-10 stays open at **35% estimated work remaining**. Large-load
testing remains disabled until after the operational MVP milestone.

### Match-ANY filter submission admission (2026-10-15)

Retained run `38670117926`, job `116225733787`, passes 102 cases and fails the
phone Card-label scenario. Browser artifact `11949094788` is independently
verified against its 8,594,660-byte size and GitHub SHA-256. The trace shows the
original filter change and its deliberate original-key recovery both succeed.
After selecting Match ANY, a protected Board read starts just before Enter on
Apply. No third filter-change POST is sent. The snapshot retains Match ANY in
the field and explicitly identifies the results as the last applied filters.
The failing source file is unchanged between that candidate and the repair's
parent revision; an unchanged local reproduction is not claimed.

The second Apply now uses the existing bounded `pressObservedRequest` helper,
with an observer counting actual browser apply-request starts. It stops once
the third request starts, including while its response is pending. Original
filter acknowledgments, exact four-change assertions, key/source identities,
persistence, label operations and the 90-second deadline remain unchanged.

The complete Card-label and irreversible List-deletion files pass together at
desktop/phone: **4/4 in 206,684.21 ms**, no unexpected/flaky/skipped outcomes or
whole-test retries. They run against archive-verified retained images at
`24f562464f706884e4a27577b61c65ff385b4263`, with fresh PostgreSQL 17/pgvector,
all 149 migrations and restricted API/Worker roles. All 172 captured inputs
match; unchanged image identities and restored full Docker inventory are
checked, and 1,699,129 owned temporary bytes are removed. No image is built,
downloaded or imported; the existing local database is unused. Browser types
and diff checks pass. The API fixture permits unverified accounts and disables
mail delivery; the Worker retains its verified-account policy. This does not
certify current-main images, full CI or managed providers. PRD-10 stays open
at **35% estimated work remaining**. Large-load testing remains post-MVP.

### Refusal-reference reload observation (2026-10-15)

[Run 38678781079](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38678781079),
job `116270766825`, passes 95 cases and fails desktop label-reference recovery.
Artifact `11958695548` is independently checked against its 5,048,689-byte size,
SHA-256 `a5b795cbd8dc9500a3095917c441d9ede4a31fd1bb3d2e4d54fdee242dbceb38`
and embedded candidate metadata. After Reload receives focus/Enter, its trace
contains Board reads but no later label-directory read. The dialog retains the
original conflict and reference. This identifies an unobserved reload; it does
not establish a product cause. The failing file is unchanged from candidate
`5218d8716088505151ffe6d0bd264771b0a7d158` to the repair's parent.

The read-only Reload step now uses the existing bounded opening helper and
observes the actual canonical Edit control. The original single refused PATCH,
real competing edit, canonical contents, reference withdrawal, accessibility,
viewport checks and 90-second deadlines remain unchanged. Mutation submission
is unchanged.

Both the unchanged baseline and revised complete desktop/phone read/edit file
pass **4/4**, in **174,686.175 ms** and **174,978.963 ms**, respectively. Each
case has one attempt, no skips, flaky/unexpected outcomes or report errors. An
unchanged local reproduction is not claimed. Browser types pass. The revised
capture matches all 172 captured browser inputs and three web configuration
files. It uses archive-verified retained `24f5624` images, fresh PostgreSQL with
149 migrations and restricted API/Worker roles; current Nginx configuration is
mounted read-only. API verification is optional and mail disabled; the Worker
retains its verified-account policy. This is supporting functional evidence,
not certification of current-main images or managed providers. No images are
built, downloaded or imported; the original database is unused. Owned resources
are removed, full Docker inventory restored and 1,708,149 temporary bytes
removed. PRD-10 remains open at **35% estimated work remaining**. Large-load
tests remain disabled until after the operational MVP milestone.
