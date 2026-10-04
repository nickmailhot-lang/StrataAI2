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
  Their new execution is pending. File downloads, previews and covers still need
  explicit moved-entity coverage.
- Update notification admission/projection: the existing inbox joins current Card
  Board to historical source Board, so moved notifications remain hidden until
  a current-authorized projection is implemented.
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
