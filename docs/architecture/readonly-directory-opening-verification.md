# Checklist and invitation directory opening verification

This supports the [operational MVP milestone](operational-mvp-milestone.md), PRD-13 and PRD-60. It does not establish release acceptance.

## Retained hosted evidence

[CI run 38294597146](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38294597146), commit `7beb325cd5f402bbd26fbdbb95303a4ff5f81afb`, fails the desktop checklist-item creation scenario before the empty item page and the desktop invitation-history scenario before its heading. The retained artifact's build SHA is verified. Its traces show opening keypresses but no following checklist-item directory read or invitation-history directory read. These failures precede the scenarios' writes.

The checklist scenario now observes the empty item page within the existing five-second admission budget, progressing only unopened checklist/item disclosures. If the parent disclosure retires during admission, the directory can reopen before the item page is observed. Creation, original retry keys/bodies, persisted item text/progress, two-session changes and accessibility checks remain unchanged.

Invitation-history navigation now observes its destination heading. It focuses and activates the source link only while that link is visible and enabled; once navigation retires the link, it waits for the heading rather than refocusing a missing source control. Revocation, consent, expiry formatting, original lost-acknowledgment recovery and later authorization checks remain unchanged. No command retry or case-deadline expansion is introduced.

## Local verification, October 12, 2026

- First changed capture: **3 passed / 1 failed**, 141,785.445 ms. Both checklist cases and desktop invitation history pass; the generic opening helper fails mobile navigation by waiting for the retired source link. This is an incomplete-repair result, not a baseline reproduction of the hosted failures.
- Final complete two-file capture: **4 passed**, zero skipped/flaky/unexpected, **140,282.719 ms**. Original case deadlines and pacing remain intact.
- Existing checklist/member/invitation component checks: **90 passed**, zero failed/pending. Browser types, terminal fresh solution builds and **213** integration/shard coverage guards pass.
- Real restricted PostgreSQL/RLS, compiled Production API, separate Worker and Nginx are used. API registration keeps the full-suite unverified-account profile; Worker verification remains enabled. This does not prove strict account-verification or managed-mail acceptance.
- Primary compiled inputs are mounted read-only and held frozen during capture. Independent final checks match **2,095** hosted source/runtime files, **1,414** native source files and all copied web assets. All owned containers, database, roles and private credential files were removed; original stopped PostgreSQL and its volume remain intact.

Private reports remain outside the repository under `ci-readonly-directories-hosted-live1/live2-20261012` and `ci-readonly-directories-native-browser-live1/live2-20261012`. Mounted local builds are supporting evidence, not immutable-image certification. Current full CI and original acceptance remain pending. Estimated work remaining stays **PRD-13 33%; PRD-60 18%**. Both issues remain open.
