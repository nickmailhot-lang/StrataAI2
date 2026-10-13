# Board starring review opening verification

This supports the [operational MVP milestone](operational-mvp-milestone.md) and [personal Board starring](../board-star-preference.md).

The retained artifact from [CI run 38287805799](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38287805799) identifies commit `1d03b0c9ec56d50c336f319099fe37922a174739`. In its failing desktop Board-starring case, the launcher is enabled before Enter and disabled during activation; the expected personal-state panel does not appear.

The browser scenario now uses the existing five-second read-only opening helper to observe the actual Board starring dialog, both on the three initial clients and after reload. Only the panel opening may reactivate. Star/unstar commands, lost-acknowledgment recovery, original keys, private actor isolation, socket delivery, canonical-state comparisons and 120-second case deadlines are unchanged.

## Local verification, October 12, 2026

The changed complete scenario passes **2/2** desktop/mobile cases in **106,016.319 ms**, with zero skipped/flaky/unexpected cases. It uses restricted PostgreSQL/RLS, compiled Production API, separate Worker, Nginx and verified disposable fixture accounts. A fresh solution build completed before capture; Primary remained frozen until completion. Independent checks match all **2,090** hosted source files, **1,414** native source files and all copied web assets. Owned containers, database, roles and private credential files were removed; original stopped PostgreSQL and its volume remain intact.

Board starring component checks pass **27/27**, with zero failed/pending. Browser types and **213** CI guard checks pass. An earlier ordinary capture passed its two browser cases but overlapped a rebuild and had nine compiled-output hash mismatches; it is excluded from acceptance evidence. No paired locally reproduced failure is claimed for the hosted interruption.

Private final reports remain outside the repository under `ci-board-star-opening-hosted-live2-20261012` and `ci-board-star-opening-native-browser-live2-20261012`. Mounted local builds do not certify immutable release images. Exact-current CI, retained-image release evidence and remaining original PRD acceptance are still required. Estimated PRD-04 work remaining: **15%**, unchanged; the issue remains open.

## Personal history opening

The retained full-1 browser artifact from [CI run 38310357937](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38310357937)
identifies `32cda04fae03ba009fe46381afa9109c9d3afbfe`. Its desktop case reaches
personal history review but receives zero displayed rows instead of one. The
failing browser context does not issue a star-history request after its opening
keypress. This supports an interrupted read-only opening; it does not establish
the exact hosted cause or a paired locally reproduced failure.

The original complete scenario now uses the same five-second read-only opening
helper for each client's personal history region. Only opening may reactivate.
Star/unstar commands, two original-key replay attempts, actor isolation,
persisted event counts, timestamp/timezone captions, withdrawal, accessibility
and the 120-second case deadline remain unchanged.

Existing star-control/history component checks pass **42/42**, browser types and
a fresh terminal solution build pass, and **213 CI coverage guards** pass.
The complete desktop/phone scenario passes **2/2** in **107,899.723 ms**, with
zero unexpected, skipped or flaky cases. An earlier fixture used strict API
verification with fixture activation disabled and failed at sign-in with 403;
it is excluded from behavior evidence. The corrected run enables verified
fixture accounts and both API and Worker verification. Restricted PostgreSQL,
the separate Worker with discovery enabled, actual compiled API and Nginx/MUI
output are used. Independent checks find zero differences across **2,099 hosted
source/runtime files**, **1,414 native files** and all **four web assets**.
Owned fixture containers, database, roles and credentials are removed; original
PostgreSQL is preserved. Private reports remain outside the repository under
`ci-star-history-opening-hosted-live2-20261012` and
`ci-star-history-opening-native-browser-live2-20261012`.

This is compiled-host evidence, not retained-image release certification.
Estimated PRD-04 work remaining stays **15%**; all original acceptance criteria
and complete exact-current CI remain required before closure.
