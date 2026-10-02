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
runtime evidence for these new cases remains pending Linux CI.

This server increment does not complete LIST-FR-005's UI workflow or PRD-07.
Reviewed desktop/mobile controls, browser recovery/accessibility and the wider
copy, cross-Board movement, watching, telemetry and scale requirements remain
part of the original open ticket scope.
