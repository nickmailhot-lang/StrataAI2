# Cross-Board Card movement: implementation dependencies

PRD-08 requires destination permissions and Board-scoped reference integrity;
PRD-15 requires readable historical activity after actual movement. The move
command now supports same-Organization cross-Board movement in both stores.
Full MUI, native interaction, consumer and capacity acceptance remains open.

## Command contract and reference policy

Cross-Board requests provide `sourceBoardId`, `destinationListId` and the expected
Card version. Keep the original source in retries, even after the Card moves.
Legacy same-Board requests retain their original fingerprints. The transactional
decorator discovers original source, destination and current Card Boards, locks
all distinct gates in canonical order, and rechecks active parents, tenant,
issuing session and edit permissions after waits. Receipt admission performs the
same current checks before returning the original acknowledgement.

The Card keeps its ID, dates, content and Card-owned objects. Its Board, List,
rank, route and version change atomically. Active source labels are cloned with
new destination-scoped IDs, names and colors. Assignments retain only active
Organization users who are active destination Board members and satisfy the
configured email policy; retained assignment attribution and creation timestamps
are preserved and their association revision increments. Other current
assignments are removed, without deleting historical notifications.

One Card revision and one mutation audit accompany two body-free `CARD_MOVED`
events, one in each Board stream, with distinct event IDs. Destination Watch
production uses current eligibility; the source event invalidates its canvas.
Personal Reminder owners are rechecked against current destination visibility,
account and Organization membership. Ineligible owners are suspended and their
generation changes; eligible owners keep queued overdue attempts when the date
and lifecycle have not changed. Reminder identity remains stable.

API tests exercise stable identity, label/assignment policy, personal Reminder
retention/suspension, original-source receipt recovery after later edits, revoked
source access, and same-tenant admission. The mandatory exact-image movement
fixture also forces event publication failure after reference/Card writes and
requires complete rollback before retry. Local compilation and shell syntax
validation are available; these new execution cases require Linux CI results.

## Historical notification storage

Migration `066_notification_historical_card` separates the immutable source Board
from the stable Card's current Board. Notifications keep their original Board,
event, actor, recipient, Card version and timestamp. They still reference the
same-tenant Card and the original source event/type. An additional typed source
constraint binds a Card event's entity ID to the notification's Card ID, or a
Reminder event through its immutable Reminder-to-Card reference. An unrelated
same-tenant Card cannot replace the historical subject. Valid existing rows are
checked during upgrade; an AFTER trigger validates inserts and changed source
identities once generated event-type fields are available. Read-at changes retain
the admitted source identity.
Current API/Worker grants and forced RLS remain unchanged.

The populated migration runner inserts a valid historical notification before
the upgrade and compares its complete JSON envelope after two migration runs.
It first injects a source mismatch accepted by the former same-Board FK and
requires upgrade refusal with all DDL and ledger changes rolled back; only the
disposable fixture is then repaired before verifying the successful upgrade.
The restricted storage contract then moves a real database Card to another
same-tenant Board after clearing its fixture's current assignments, proves all
notification envelopes unchanged, rejects same-tenant subject rebinding, retains
tenant isolation, and permits read-at acknowledgement without changing source
attribution. This database fixture does not prove the authorized move API or
assignment migration policy. Linux CI must execute these new SQL cases.

The runtime schema requirement and exact-image missing-migration/refusal/restore
fixture require all 66 real migrations. The runner's synthetic serialization,
failure and unrecorded fixtures are numbered 067–069.

## Remaining consumer and acceptance work

- Verify the new authorized command and rollback fixtures execute successfully
  in the full immutable release pipeline.
- New HTTP and exact-image consumer cases now preserve Checklist/item, URL
  attachment and comment records plus personal Card Watch identity; source-only
  readers and edits are refused under current private destination admission.
  The exact-image child fixture passed in run 37196075598. A Linux HTTP fixture verifies stable file/preview bytes and refuses
  source-only readers before provider access, using explicitly synthetic Clean
  and publication metadata with real private byte staging; its Linux managed job
  passed in run 37197641310. The fixture now also checks selected cover delivery,
  stale Card revision refusal, PUBLIC destination PNG-only exposure and private
  destination re-admission. This cover extension uses synthetic selection and
  requires its own Linux execution. Restricted Worker scan-after-movement passed
  as described below; real preview generation and pending-upload recovery still
  need explicit moved-entity coverage.
- The inbox projection now preserves the historical source Board and binds its
  link to the current Card Board. Both contexts must be currently visible before
  pagination and are gated/rechecked before disclosure or read acknowledgement.
  New movement/read-receipt execution is pending. The release fixture also
  observes a live canonical Board gate wait, withdraws destination membership,
  and requires refusal without disclosure or command effects.
  The new post-wait setup initially failed because its psql marker was escaped
  incorrectly. Commit `abc4808` fixed the marker and used the valid `REMOVED`
  Board membership state. The full cross-Board command/inbox fixture then passed
  on exact images in container job 111426869272 of
  [run 37198597943](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37198597943).
  The broader release/native gate was still live when inspected.
- The MUI cross-Board selector now loads authorized destination Boards and
  checks an active destination List, binds account identity and original source,
  and keeps receipt recovery after canonical source removal. Unit and integrated
  Board tests cover that recovery, access refusal and stale reviews. Native
  keyboard scenarios at 1280/390 pixels exercise both streams and lost replies;
  actual native execution and concurrent/reconnect acceptance remain required.
- Verify supported capacity, visual feedback and acknowledgement budgets before
  closing PRD-08/15.

Baseline `850daff` passed web, managed/API, PostgreSQL, source-quality, image build
and security jobs in run 37193132162; container integration was still running
when this implementation began. No issue is closed by this stage.


The command revision `73812de` passed web, managed/API, PostgreSQL, source-quality
and immutable image build jobs in run 37195010808. Release integration and
security were still running when the MUI continuation was prepared. Local MUI
checks passed 60 related tests before the final integration case; the final
42-test selector/Board rerun and separate integrated recovery case passed.
No native execution is inferred from Playwright test discovery.


## Supported-capacity command gate

The capacity chain now includes 20 actual serial cross-Board commands through
Nginx and the restricted immutable API. Both Boards have 200 Lists; the source
starts with 5,000 active Cards and the destination with 4,999, so every alternating
move finishes with exactly 5,000 active Cards at its admitted destination. The
Organization retains the existing 100,000 archived Cards. The moved Card carries
one active label and one explicit eligible assignment, plus the existing large
comment/checklist history. The fixture verifies stable body/identity, 40 Board
events, 20 audits/receipts and unchanged original receipt recovery after 19 later
moves. The retained fixed-field artifact records all 20 HTTP timings and enforces
nearest-rank p95 below 500ms. It also retains a failed budget measurement to aid
repair. This is one serial client with no intentional network latency; it does
not prove browser feedback, concurrent-client or cached-open budgets.

The exact-image capacity step passed on commit
`ce8d0909ca842bb5dbedffa43558c658ca702645`,
[CI run 37196290255](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37196290255).
Retained artifact `card-move-capacity-ce8d0909ca842bb5dbedffa43558c658ca702645`
(artifact 11301523832) reports all 20 samples and mutation p95 **49.962ms**,
below the unchanged 500ms budget under the conditions above. The full release
gate was still running when this artifact was inspected; this evidence supports
the command capacity requirement only.


The mandatory exact-image command fixture also submits opposing A-to-B and
B-to-A moves simultaneously on two distinct Cards. It requires both commands to
commit with the correct routes/revisions, four Board events and two audits. This
exercises canonical gate ordering with actual PostgreSQL; execution is pending.


## Historical inbox projection contract

Stored notification envelopes remain unchanged. `boardId` continues to denote
the historical source. Only a moved item adds `currentBoardId`; its `entityLink`
uses that current Board. Same-Board responses preserve their previous shape.
The MUI parser validates the current Board GUID and binds the exact same-origin
Card link to it, preserving the historical source separately. A source-only or
destination-only reader cannot disclose a moved historical item. SQL applies both
Board visibility predicates before its 51-row seek window. The command gathers
all historical/current gates in canonical order and verifies the same current
routing after waits; a movement during planning is refused rather than admitted
under an unheld Board. Read and exact read-receipt retries use the same rule.
Organization, account, email-policy, Card/List lifecycle, recipient, private
source and Owner Portal boundaries remain enforced. New API, parser and release
fixtures cover moved links, hidden ineligible recipients and source revocation.

## Pending file scan after movement

`AttachmentWorkerContract` also claims and loads a pending file scan, moves its
parent Card using trusted SQL fixture setup, then executes the real restricted
Worker delivery adapter. The contract requires the same file/uploader identity,
Clean status, exactly one additional Card revision, one ready event on the
current destination Board, one audit, and replay without repeated storage or
scanner I/O. A PDF MIME fixture avoids introducing an unrelated preview job.
The parent route is restored before subsequent lifecycle contracts.

This proves Worker routing independently of movement authorization. Actor
admission, routing setup, object bytes and the scanner are synthetic; it does
not establish HTTP/session admission or deployed storage/antivirus acceptance.
The fixture compiled with zero warnings/errors at `6c5f91e` and executed
successfully in the completed PostgreSQL job (111424468186) of
[CI run 37198252289](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37198252289).
The full release/native gate remains pending.

## Original recovery keyboard admission follow-up (2026-10-14)

Retained run `38505687589`, shard 1 job `115689442230`, fails the phone
acknowledgment after source removal. Its trace contains only the first move
POST, deliberately lost after the actual commit; focus and Enter occur on the
retry button without a second POST. Current unchanged local desktop/phone
scenarios pass **2/2 in 109,761.524 ms**, so no local hosted-failure reproduction
or new product root cause is claimed.

Initial confirmation and original recovery now use the existing observed-request
admission helper. Keyboard activation stops when the intended POST is observed,
including while its response remains pending. The exact original body/key,
two total requests, canonical source disappearance, destination placement,
two real peer streams, activity facts/current Board attribution, focus,
reload/overflow and original 150-second scenario deadlines remain unchanged.

The complete changed desktop/phone file passes **2/2 in 109,064.009 ms**, zero
skips, unexpected/flaky results, retries, report errors or diagnostic hooks.
Browser types, 224 CI/shard/startup guards and all 398 cases across intact shards
pass; collection is not execution. Independent verification matches 2,143 hosted
inputs, 1,441 native inputs, 168 frozen browser files and four served assets.
Owned fixtures, credentials and newly pulled support images are removed;
original stopped PostgreSQL is preserved. These compiled-runtime/controlled-
account results do not certify current immutable/full CI or managed providers.
Estimated PRD-08 work remaining stays **35%**; large-load testing remains
disabled until after the operational MVP milestone.

## Admitted Card activity review after cross-Board recovery — 2026-10-16

Hosted [run 38809952751, browser shard one](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38809952751/job/116786812691)
at `2c4e0b2a53b6b200df733f62f02189ea05ae7b58` reports 102 passes and one
desktop failure: the expected two activity entries are absent. The original
diagnostic artifact **12026775307** is independently verified at **10,912,314
bytes**, SHA-256 `6f259974a9a941992c2ca9c668db72311b1d964f8b62982398dc0a0090bef49d`.
Its trace records the activity-button click, but no Card-activity HTTP read;
the screenshot still shows **Review Card activity** and no opened history region.
The test and activity component remain byte-identical to that failed revision
before this change. This locates an unobserved opening transition; it does not
identify the precise event that prevented activation or prove missing backend
history. No local reproduction of that specific click loss is claimed.

Both initial and post-reload activity openings now use the existing bounded
nonmutating keyboard-review admission helper and observe the actual Card
activity region. An unopened region still fails within the original five-second
admission budget. Mutation/recovery retain their observed-request helper, exact
original body/key, two POSTs and all current source/destination/stream/history/
focus/reload/overflow assertions. The original 150-second scenarios and default
activity-count assertion timeout remain unchanged; no product code changes.

All **11** keyboard-admission protocol tests pass, including five new inline-
review cases: successful opening, ignored first activation, already-open/retired
launcher, opening during focus and failure when the region never opens. Browser
types, collection and all **285** CI integration guards pass. Both maintained
desktop/phone native scenarios pass on single attempts in **100,878.640 ms**,
with zero unexpected/flaky/skipped cases or report errors. Actual restricted
PostgreSQL 17.11/all 149 migrations, current API/Worker assemblies and current
Vite assets verify both real peer streams, exact original recovery, two canonical
CARD_MOVED activity facts with current destination attribution and reload.

All **1,272 source/fixture inputs, 71 binaries and four served assets** match.
The API process remains stable. The original scoped-Worker helper recreates the
Worker as designed; the final Worker uses the same cached image, is healthy and
has no Organization filter. Private Compose files contain environment placeholders
only, retain the checked-in Organization-scope overlay and remain unchanged
during execution. Owned cleanup removes **3,308 temporary bytes** and restores
the original Docker inventory. The verified download archive is also removed;
only its selected small failure diagnostics remain private. No image archives,
new images, existing databases or large-load tests are used.

This uses cached runtime images and current read-only assemblies/assets, with
local metadata `2b59fd0c`/`1.0.0`, controlled provider/optional-email policy and
private fixture composition. It does not certify current immutable/full CI or
managed providers. PRD-08 remains open at **35% estimated work remaining** and
PRD-15 at **34%**. Large-load testing remains disabled until after MVP.
