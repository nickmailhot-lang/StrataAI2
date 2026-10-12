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
