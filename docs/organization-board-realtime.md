# Organization Board archive realtime acceptance

PRD-04 requires other authorized clients to receive or recover relevant shared
Board changes. The archive directory currently uses foreground/poll recovery;
its Organization-wide SignalR subscription is still unfinished.

Migration 073 introduces an Organization-ordered projection of the six canonical
shared Board lifecycle/metadata events. The rows reference existing Work event
IDs; no new audit history, fake actor, copied metadata, star preference or
Card/Watch/Reminder activity is manufactured. A narrow parent-row trigger
projects actual source insertion in the same transaction, including rollback
and duplicate-source semantics. Its tenant counter serializes cross-Board
publication before commit. Historical backfill has a deterministic created-time
and event-ID order and does not rewrite source history. Migration installation
fences source writers through backfill/trigger creation.

Both tables have forced tenant RLS and tenant-affine foreign keys. Runtime API
access is SELECT only; the Worker keeps its existing narrow readiness capability
on the original event. The hardened trigger capability derives every effect
from the actual inserted parent and is not directly callable by runtime roles.
A journal row remains pending until its canonical source becomes ready. This
projection alone is not authorization to disclose a Board or its envelope.

The required PostgreSQL source test exercises restricted insertion, two Board
streams sharing one Organization order, another tenant, original event IDs,
pending/readiness, a declared late owning rollback, duplicate insertion,
no direct runtime journal/counter writes, immutable history and private-star
exclusion. Populated migration upgrade/repeat coverage requires exact eligible
source counts, matching stream heads and unchanged source bytes. The existing
raw activity-source contract cleans its explicitly synthetic history through
an administrative transaction, including its dependent projection, and restores
the guard before commit; this does not expose a runtime retention path.

Strict local .NET build and migration-runner syntax checks pass. Actual new
migration/storage execution is pending CI. Concurrent commit-order testing,
current actor/Organization/qualifying Board administration before and after IO,
privacy-bound cursor recovery, bounded delivered reads, the demo adapter,
SignalR session withdrawal and the MUI archive consumer still need implementation
and executed acceptance. No raw journal endpoint is exposed. Current foreground
polling must not be described as completed Organization live updates.
