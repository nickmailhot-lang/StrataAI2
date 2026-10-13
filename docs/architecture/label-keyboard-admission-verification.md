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

Two additional private controlled probes are excluded from regression proof: an unsynchronized refresh passes the old opening, while a refresh scheduled before the original press reaches the original scenario timeout without proving the hosted failure. Neither establishes a before/after result. The hosted phone Board metadata focus failure also remains unresolved. Mounted local runtime evidence does not certify the immutable CI images; complete current CI and the original acceptance requirements remain necessary. Estimated PRD-10 work remaining stays **35%**; the issue remains open.
