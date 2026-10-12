# Archived Card recovery admission verification

This supports the [operational MVP milestone](operational-mvp-milestone.md) and [archived Card directory](../archived-card-directory.md).

The retained artifact from [CI run 38287805799](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38287805799) identifies commit `1d03b0c9ec56d50c336f319099fe37922a174739`. The mobile scenario fails at the refresh-button focus assertion after lost restore acknowledgment. The trace shows Retry enabled before Enter and disabled during activation. No recovery POST follows that keypress. Empty canonical discovery does not establish receipt recovery: the unresolved dialog remains above the archive and its refresh target is unavailable to the role query.

The scenario now observes the original recovery request within the existing five-second admission budget and stops activation when the route has observed its second attempt. It explicitly requires the recovery dialog to close before asserting the empty archive and returned focus. Both requests must retain the same original key/body. Original parent-change checks, other-client canvas, neighbor equality, Card version/rank and post-reload assertions remain intact, with the same 120-second case deadlines and pacing.

## Local verification, October 12, 2026

- Complete desktop/mobile scenario: **2 passed**, zero skipped/flaky/unexpected, **97,997.475 ms**.
- Existing archived Card component checks: **35 passed**, zero failed/pending; browser types, fresh solution build and **213** CI guard checks pass.
- Restricted PostgreSQL/RLS, compiled Production API, separate Worker and Nginx are used. The API permits the scenario's original unverified registrations, matching its full-suite profile; Worker verification remains enabled. This does not establish strict account-verification or managed-mail acceptance.
- Fresh build completed before capture. Independent checks match **2,091** hosted source files, **1,414** native source files and all copied web assets. Primary remained frozen until terminal completion. All owned containers, database, roles and private credential files were removed; original stopped PostgreSQL and its volume remain intact.

Private reports remain outside the repository under `ci-archived-card-recovery-hosted-live1-20261012` and `ci-archived-card-recovery-native-browser-live1-20261012`. No paired locally reproduced-failure claim is made; the change addresses retained hosted evidence and passes the complete ordinary local scenario. Mounted builds are supporting evidence, not immutable-image release certification. Current full CI and remaining original PRD acceptance remain pending. Estimated PRD-18 work remaining: **15%**, unchanged; the issue remains open.
