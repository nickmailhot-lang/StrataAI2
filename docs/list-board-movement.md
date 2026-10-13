# List movement between Boards (PRD-07)

[PRD-07](https://github.com/nickmailhot-lang/StrataAI2/issues/8) includes moving a List to another authorized Board. Existing List reordering and copying do not satisfy that requirement. The implementation is in progress; this document preserves the full movement scope while recording the first Domain increment.

## Canonical Domain transition

`BoardList.MoveToBoard` changes the parent Board and normalized rank within the List's existing Organization. Identity, Organization, name and creation history remain unchanged; one successful transition advances its version and update timestamp once. Empty destinations, different/empty Organizations, empty ranks and backwards timestamps are rejected before changing the parent or rank. Caller-supplied destination context is not authorization: the Application service must resolve and admit both real Board scopes before invoking a transition.

Nine focused Domain cases cover identity/history preservation, subsequent movement and complete unchanged state for invalid scope, destination, rank and time. Together with the four existing canonical hierarchy cases, **13/13 pass**, zero skipped or failed. A fresh solution build succeeds with no warnings/errors; all 213 immutable CI coverage guards pass. These checks establish the entity transition, not a working transfer endpoint or full PRD acceptance.

## PostgreSQL parent-integrity prerequisite

Migration `147_list_board_move_parent_integrity` makes the composite Card-to-List parent constraint deferrable while retaining its **initially immediate** behavior. Ordinary writes still reject parent mismatches immediately. The future owning transfer transaction can explicitly defer this one constraint while updating the List and its contained Cards; an inconsistent final graph still cannot commit. The runtime schema check now requires migration 147 before accepting Production traffic. Apply the migration before starting the new hosts, as with the existing release procedure.

The real PostgreSQL fixture upgrades schema 146 with existing active, archived and deleted Cards. A complete graph hash remains unchanged by the upgrade. Through an authenticated restricted API login, it verifies immediate refusal, incomplete deferred-commit refusal with complete rollback, cross-tenant parent refusal and write denial, missing-context withdrawal, preserved forced RLS and successful complete parent commit across all three lifecycle states. Stable identities, content, ranks, lifecycle and creation history remain unchanged; List/Card versions advance once. Later transactions retain immediate validation. Another tenant's data remains unchanged.

The mandatory CI case uses a uniquely owned, network-isolated PostgreSQL/pgvector container, temporary filesystem and private credentials. Four new CI guard cases reject removal, replacement, skipping or ignored failure of that check. Final verification passes the comprehensive database case, existing production-role provisioning regression, 13 Domain/hierarchy cases, 25 release-integrity checks, 217 CI coverage guards and two startup guards, plus workflow validation and a fresh solution build. Owned containers and credentials were removed; the original stopped PostgreSQL data was preserved. An initial thin-role fixture lacked required routing grants and is excluded; final results use the actual provisioned API login.

This establishes the storage constraint prerequisite, not transfer authorization, the complete reference graph, HTTP behavior or retained-image release acceptance. Current immutable CI and the remaining layers below still govern completion. Estimated PRD-07 work remaining stays **35%**.

## Remaining implementation

- Admit current move rights and active lifecycle in both Boards, bind the actual Organization and version, and preserve neutral refusals after permission/session changes.
- Lock both Board scopes in a consistent order and move the complete List graph atomically under forced PostgreSQL RLS. Preserve stable identities and explicit lifecycle behavior for active, archived and deleted contained records; satisfy parent constraints for Cards and their Checklists, comments, labels, members, attachments/covers, dates, reminders, watch routes and history. Apply existing Board-specific association and source-admission rules explicitly rather than dropping record classes.
- Handle configured intake-List bindings through the owning Organization-configuration contract or a safe refusal. Moving a List must not bypass configuration authority or silently invalidate its binding.
- Allocate destination ordering and enforce supported List/Card capacity with concurrency and exhausted-rank rollback. Include large contained/archived datasets; movement is not limited to empty Lists.
- Persist original request receipts with atomic audit/outbox publication, unchanged recovery after lost replies and current-source/destination admission on replay.
- Invalidate both Board streams, reconcile source/destination watches, search, notifications and protected references, and verify peer recovery after disconnect.
- Expose the admitted API and MUI destination/review/confirmation workflow with retained drafts, keyboard focus and tablet/phone behavior.
- Execute Domain, Application, restricted PostgreSQL, HTTP and retained-image browser matrices, including revocation while waiting, late publication rollback and original-key recovery.

The existing [Card movement](card-movement-ui.md), [List copying](list-copy.md), [List ordering](list-position-persistence.md) and [permission acceptance](architecture/prd-05-acceptance.md) supply related contracts. The [operational MVP milestone](architecture/operational-mvp-milestone.md) retains its complete release gates. Estimated full PRD-07 work remaining: **35%** (provisional planning estimate, including unfinished transfer and full acceptance). No requirement or issue is complete based on this Domain increment.
