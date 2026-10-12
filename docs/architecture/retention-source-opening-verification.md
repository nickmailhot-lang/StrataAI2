# Retention source opening verification

This preserves an existing safety workflow while supporting the [operational MVP milestone](operational-mvp-milestone.md). It adds no governance module and does not establish full PRD-70 acceptance.

## Retained hosted failure

[CI run 38294597146](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38294597146), commit `7beb325cd5f402bbd26fbdbb95303a4ff5f81afb`, fails mobile three-source classification while waiting for `Retention classification for this Comment`. The downloaded trace's build SHA is verified. A Comment read succeeds earlier in the scenario, but no new Comment read follows the failing review activation. The missing launcher precedes classification review, consent and submission.

`sourceEntry` now observes the admitted `Source retention classification` region within the existing five-second preparation budget. It focuses and activates only a visible, enabled, unopened classification entry or its Comment/Attachment disclosure. While the view is loading or the launcher has retired, it waits for the admitted region. Classification review, explicit consent, original retry key/body, source/actor/version checks, audit event identity and later permission withdrawal remain unchanged. No mutation retry, case-deadline increase or removed test is introduced.

## Local verification, October 12, 2026

- Complete original browser file: **14/14 cases pass**, zero skipped/flaky/unexpected, **588,176.247 ms**. Both widths cover hold administration/recovery, membership withdrawal, archived source behavior, original recovery after a Card move and actual Card/Comment/Attachment classification with exact reload recovery. Original case deadlines and pacing remain.
- Existing source classification/entry/Comment components: **63 passed**, zero failed/pending. Fresh terminal solution build, browser types and **213** integration/shard coverage guards pass.
- Real restricted PostgreSQL/RLS, compiled Production API, separate Worker, Nginx and strict API/Worker account verification are used. Existing disposable-account activation does not establish managed-mail delivery.
- Primary compiled inputs are mounted read-only and Primary remains frozen through terminal completion. Independent checks match **2,097** hosted source/runtime files, **1,414** native source files and all copied web assets.
- All owned containers, database, roles and private credential files are removed. Original stopped PostgreSQL and its volume remain intact.

Private reports remain outside the repository under `ci-retention-source-opening-hosted-live1-20261012` and `ci-retention-source-opening-native-browser-live1-20261012`. No paired local reproduction of the original hosted failure is claimed; this complete ordinary capture supports compatibility. Mounted builds do not certify immutable release images. Exact-current full CI, remaining owning producers, privacy/redaction/export/disposition and original PRD acceptance remain pending. Estimated PRD-70 work remaining: **60%**, unchanged; the issue stays open.
