# List copying

`POST /lists/{listId}/copy` accepts `destinationBoardId`, `name` and the source
List `version`, with the established optional `Idempotency-Key`. The destination
may be the same Board or another editable active Board in the same Organization.
The source List must be active and both Boards must grant current edit rights.
Inaccessible, deleted, archived or cross-Organization contexts use a generic
`list_not_found` response. A stale first submission produces `version_conflict`;
the new name uses the existing List validation rules.

The new List is appended after active destination Lists with a new stable ID,
version 1 and new timestamps. Every non-deleted source Card gets a new ID,
version 1 and new timestamps, retaining its title, description, rank and active
or archived state. Archived copies receive their own archive timestamp. Deleted
Cards are excluded. Source entities and sibling ranks are unchanged. This is a
copy of the currently implemented canonical Card fields, not a claim that future
labels, members, attachments, checklists or other PRD data have been implemented.
Those features must extend this copy path before their acceptance is complete.

Both Board command scopes are locked in stable ID order. Authorization, active
Organization membership, session validity and parent lifecycle are rechecked
after waits and before historical receipt disclosure. The PostgreSQL command
owns one tenant transaction for the List, all copied Cards, `LIST_COPIED` audit
and durable event/job, and the retry receipt. Audit failure rolls back the copy
and claim. Identical retries return the original created response without
duplicating entities or effects; a changed key fingerprint is rejected. A newer
source version does not replace a committed historical acknowledgment.

The event invalidates the destination Board through the existing separate Worker
delivery path. Its envelope contains no copied content. Operator metrics use the
fixed `list_copy` operation with the existing bounded, content-free labels.

`ListCopyTests.cs` covers canonical data/order/lifecycle/new identities, identical
receipt recovery after source rename, changed intent, stale version, private
denial, archived source and cross-Organization rejection. The required exact-image
`test-archived-lists.sh` fixture additionally covers cross-Board persistence,
complete audit-failure rollback, archived timestamps, single event/audit effects,
post-wait destination lifecycle loss and historical current-authority checks.
The solution and tests build with zero warnings/errors and shell syntax passes;
Linux CI run [37009610022](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37009610022)
at `1aaeda9` subsequently passed the unfiltered Domain/API host source tests,
PostgreSQL source integration, web checks, immutable image build and security.
The new required exact-image copy database fixture remains pending its container
stage; passing host tests alone does not establish those PostgreSQL semantics.

## Reviewed Board control

The Board toolbar's `Copy list` opens a MUI review. Users choose the active
source List, a name and a visible destination Board. Before enabling confirmation,
the control reads that destination and requires exact Organization/Board scope,
active lifecycle and current edit access. The server repeats authorization during
the actual command; the review is not a grant. The review explicitly describes
new identities, inclusion of active/archived Cards and exclusion of deleted Cards.

A live source version/name/rank change preserves the name draft but blocks a first
submission until the user explicitly adopts the current List and reviews again.
An uncertain submission instead retains its original source version, normalized
name, destination and retry key outside the canvas column. It cannot be replaced
by another copy or an edited destination/name. A scoped created acknowledgment
must have a new ID, version 1, matching name/Organization/destination, active
lifecycle and valid rank. Missing/malformed acknowledgments remain recoverable.
Current access loss clears protected review and aborts/fences pending responses.
Reads and the complete acknowledgment body have a 15-second deadline. Cancel
and acknowledged completion return focus after the dialog exits.

`ListCopyControl.test.tsx` checks scoped review/receipt, unchanged recovery after
source removal, malformed receipts, draft conflict, unavailable destination,
permission loss, safe fixed errors, response-body timeout and malformed/denied
discovery. The Board integration regression checks recovery through a canonical
source revision and focus return. Their 40 focused tests pass together.
`tests/browser/list-copy.spec.ts` adds desktop/phone keyboard flows against exact
images, real cross-Board copying, lost-response recovery after source rename,
Worker delivery to another client, archive-state/order persistence and reload.
Both browser cases collect successfully; runtime evidence remains pending CI.

This does not complete PRD-07. Full browser/accessibility verification, future
Card metadata copying, cross-Board movement, watching, telemetry and scale
requirements remain part of the original open ticket scope.
