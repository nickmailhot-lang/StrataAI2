# Board deletion consent

PRD-18 LIFE-FR-006–008 apply to Board deletion as well as List/Card deletion. `DELETE /boards/{id}?version={reviewedVersion}&confirmed=true` is the explicit permanent-deletion command. Missing or false confirmation returns `delete_confirmation_required` (400) after current administration and archived-state admission. Inaccessible or non-archived Boards retain the non-disclosing `board_not_found` response.

The Application service also requires consent, so callers cannot bypass the rule by skipping HTTP. The transactional command fingerprint includes the reviewed revision and confirmation intent. A successful deletion records the authorized actor and returns the canonical tombstone revision.

The source API fixture covers active-state rejection, contributor denial without confirmation disclosure, missing/false consent with unchanged revision and attribution, direct service rejection, and confirmed deletion attribution. Compilation passes; Linux/API/PostgreSQL execution remains pending rigorous CI.

Board archive discovery, lifecycle review UI, irreversible-deletion wording, and fresh-authority tombstone receipt recovery remain unfinished. The fingerprint change alone does not establish safe replay after deletion. Retention and backup cleanup are separate unfinished policies; this command does not claim physical erasure.
