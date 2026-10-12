# Movement and peer activity admission verification

This supports the [operational MVP milestone](operational-mvp-milestone.md), PRD-06/08 and PRD-15. Full release acceptance remains pending.

## Retained hosted evidence and changes

[CI run 38294597146](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38294597146), commit `7beb325cd5f402bbd26fbdbb95303a4ff5f81afb`, fails desktop Card pointer reorder and desktop peer activity opening. The Card-move trace's artifact SHA is verified. Board admission becomes busy during the second pointer gesture; no move request follows that gesture. Its missing acknowledgment is not an ambiguous sent command.

Desktop pointer scenarios now wait for actual pending Board delivery, reload and observe protected follow-up reads and a non-busy workspace before measuring geometry. The handle establishes current enabled focus before the same single gesture. No drag retry is added. Reload also retires the earlier move notice before the independent drag scenario; actual acknowledgments and persisted ranks/versions must still pass. Original append/relative lost-response recovery, bodies/keys, other-client convergence, keyboard cancellation/drop and mobile checks remain intact.

The activity failure expects 50 peer rows but observes zero after its read-only opening. Peer preparation now observes the actual first page within the existing five-second admission budget, opening a not-yet-open review or selecting its current-page read. It stops once all 50 rows are present. Profile/timezone rebinding, immutable actor captions, pagination, reconnect and revoked-access checks remain unchanged. No command retry, row-count relaxation or case-deadline increase is introduced.

## Local verification, October 12, 2026

- Complete Card movement scenario: **2/2** desktop/mobile cases pass, zero skipped/flaky/unexpected, **99,020.219 ms**, with original 120-second case deadlines. The full-suite API permits the original unverified registrations; Worker verification stays enabled.
- Complete strict activity scenario: **2/2** desktop/mobile cases pass, zero skipped/flaky/unexpected, **134,727.876 ms**, with original 180-second case deadlines and strict API/Worker verification. Disposable accounts are activated by the existing fixture; managed mail is not proved.
- Existing drag/activity component checks: **3 + 17 passed**, zero failed/pending. Fresh terminal solution builds, browser types and **213** integration/shard coverage guards pass.
- Real restricted PostgreSQL/RLS, compiled Production API, separate Worker and Nginx are used. Primary compiled inputs are mounted read-only; Primary stays frozen during each capture. Before subsequent edits, independent completion checks match **2,096** hosted source/runtime files, **1,414** native source files and all copied web assets for each capture.
- Owned containers, database, roles and private credential files are removed after each run. Original stopped PostgreSQL and its volume remain intact.

Private reports remain outside the repository under `ci-card-pointer-admission-hosted/native-browser-live1-20261012` and `ci-activity-peer-admission-hosted/native-browser-live1-20261012`. No paired local reproduction of the original hosted failures is claimed. These complete ordinary captures support compatibility; mounted builds do not certify retained release images. Current exact-image CI and original acceptance remain required. Estimated work remaining stays **PRD-06 25%; PRD-08 35%; PRD-15 34%**. All three issues remain open.
