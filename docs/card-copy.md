# Card copying

`POST /cards/{cardId}/copy` accepts `sourceBoardId`, `destinationListId`, `title`
and the source `expectedVersion`, with the established `Idempotency-Key` header.
The source and destination must belong to the same active Organization. Both
Board contexts require current edit rights; both parent Lists and the source
Card must be active. Missing, inaccessible, archived or foreign contexts return
the generic `card_not_found` response before protected disclosure. Titles use the
existing trimmed 1–500 character validation. Stale first submissions conflict.

The copy appends to its destination with a new Card ID, rank, timestamps and
revision 1. It retains description, start/due date and timezone, while resetting
due completion. Active label associations reuse same-Board definitions or create
fresh destination definitions for a cross-Board copy, retaining name/color and
relative order. Equal names are not merged. Each current Checklist and item gets
a fresh ID, timestamps and revision 1; deleted descendants are excluded and item
completion attribution is reset. Every child is copied, including beyond the
reader's 50-row page. The source and its ranks/history remain unchanged.

The command does not inherit comments, historical events, assignees, personal
Reminders, Card Watches, attachments or selected covers. Binary duplication
requires independent upload/integrity/scan/publication identities and is not
implemented by reusing a source download grant or object reference. These copy
defaults are stated in the MUI review control; they do not imply
complete attachment-copy or whole-PRD acceptance.

One `CARD_COPIED` audit/event refers to the new Card. This birth event is new
history, rather than a copy of the source journal. Existing current destination
List/Board Watches receive one eligible notification, with normal self suppression
and source-affinity checks. No personal Watch is created for the new Card. The
event enters the existing separate Worker delivery path to invalidate the
destination canvas. API telemetry uses the fixed content-free `card_copy` name.

The command owns one tenant transaction for Card, labels, Checklists/items,
audit/event, recipient notification intents and retry receipt. All original and
current source/destination Boards are planned and locked in canonical order.
Original acknowledgments remain stable after later source/copy edits; retries
check current actor/session and every protected context before returning them.
If either Card moves while gates are being acquired, the request refuses rather
than acquiring another Board out of order. A later retry plans its new routing.

Migration `067_card_copy_notifications` adds only the new notification kind to
the existing constrained recipient store. RLS, immutable source history, typed
source event/Card relationships, recipient deduplication and runtime grants
remain intact. Runtime readiness requires the migration; forward/repeated
upgrade, missing-migration refusal and restore remain mandatory CI checks.

Host tests cover same/cross-Board copying, 62 visible checklist items across
pages, fresh completion state, no source-history/personal inheritance, source
revision preservation, current receipt recovery and revoked/invalid admission.
The mandatory immutable release fixture adds real restricted PostgreSQL writes,
sessions, destination Watch notification and full late-publication rollback.
The MUI control loads current permitted Boards and active editable destination
Lists, including the source Board. It reviews a bounded new title and the copy
defaults before confirmation, checks `/me` before and after every bounded request,
and retires original recovery on account change or terminal refusal. An uncertain
reply freezes the original title/body/key; recovery survives source removal and
does not invent a copied Card locally. An acknowledgment must identify a fresh
Card with the correct Organization/Board/List, title, rank and revision 1. The
Board owns this recovery outside its canonical Card editor and fences competing
edits. Cancellation returns focus to Copy Card; acknowledgment focuses the link
to the new Card once current Board admission has finished.

Local typecheck/lint/build and 55 related MUI/Board tests passed, including
cancellation/acknowledgment focus. Desktop/mobile native scenarios now verify keyboard review,
actual committed-but-lost response recovery, independent sessions observing the
new destination Card without changing the source, one persisted copy, new
activity and reload. Discovery verifies two cases; actual new native and final
managed/release execution require Linux CI. Local Windows managed execution is
blocked by Application Control. Copy capacity and complete PRD-wide acceptance
remain unfinished. PRD-08 remains open.
