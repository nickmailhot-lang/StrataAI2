# Board deletion consent

PRD-18 LIFE-FR-006–008 apply to Board deletion as well as List/Card deletion. `DELETE /boards/{id}?version={reviewedVersion}&confirmed=true` is the explicit permanent-deletion command. Missing or false confirmation returns `delete_confirmation_required` (400) after current administration and archived-state admission. Inaccessible or non-archived Boards retain the non-disclosing `board_not_found` response.

The Application service also requires consent, so callers cannot bypass the rule by skipping HTTP. The transactional command fingerprint includes the reviewed revision and confirmation intent. A successful deletion records the authorized actor and returns the canonical tombstone revision.

The source API fixture covers active-state rejection, contributor denial without confirmation disclosure, missing/false consent with unchanged revision and attribution, direct service rejection, and confirmed deletion attribution. Compilation passes; Linux/API/PostgreSQL execution remains pending rigorous CI.

Deletion commands have a dedicated receipt admission path. It can locate the retained tombstone, lock its Board/Organization scope, and verify current active Organization membership plus Organization or Board administration. Current account/session admission is still checked by the transaction executor before receipt lookup and return. Only a matching original consent/revision/key can return the saved deletion acknowledgment; changed intent fails with idempotency_key_reused, while a new key cannot delete the tombstone again. Ordinary Board/member lookup excludes deleted Boards in both stores.

Source API cases cover Organization and Board administrators, byte-identical replay, hidden ordinary reads, changed intent, new-key rejection and membership revocation. The PostgreSQL container fixture additionally covers audit rollback, restored authority and exactly one audit/domain deletion event. Compilation and Bash syntax pass; executed API/PostgreSQL/native evidence remains pending rigorous CI.

Board archive discovery, lifecycle review UI and irreversible-deletion wording remain unfinished. Retention and backup cleanup are separate unfinished policies; this command does not claim physical erasure.
