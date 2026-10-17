# PRD-01-VS-04 — Resume a Board or Card link after sign-in

Owning requirements: PRD-01 FOUND-FR-006/007, PRD-02 AUTH-FR-003; UXA-01–08 and UX-001/003/005/006/007/008. Dependencies are the existing sign-in/session, Board authorization and Card detail capabilities. Existing implementation is reused; this increment adds no API, schema, permission, shell or theme contract.

## Reconciled workflow and gap

Existing code already supports account authentication, Organization and Board discovery, Card creation/editing, canonical reads, deep links, idempotent retry and tenant/Portal admission. The selected gap is the incoming link's authentication boundary:

1. A signed-out private Board request deliberately returns the same 404/unavailable result as an unknown Board. BoardScreen offered Sign in only on 401, leaving that normal entry without a sign-in action. The unmodified browser probe fails at the missing link; its screenshot shows only the generic error and Retry.
2. AuthPage always navigated to `/app` after a validated login, losing the original Board/Card target. Three new component probes fail: Board return, Card return and retained destination after a lost-response retry.

The original application revision for these probes is `16bf945d8b01e61f46d973b2d01a92918d5e24f4`; new test inputs were uncommitted diagnostic probes. Failed logs/traces and their interpretation are retained. They are not current passing evidence.

## Scoped behavior

BoardScreen offers the same generic Sign in action on 401 and 404. It never uses a title or confirms record existence. The action passes only the current pathname in router history state and replaces the unavailable history entry. AuthPage honors only an exact internal Board/Card path after its existing acknowledgment validation. It replaces the sign-in history entry on a valid return. Ordinary sign-in still opens `/app`; invitation-specific callbacks retain control of their existing workflow.

The allowlist admits only fixed canonical Guid path segments. It preserves existing hash-derived creation IDs, which do not necessarily have RFC UUID version/variant bits. It rejects external/protocol-relative URLs, Portal/destructive/unrelated routes, encoded or malformed paths, queries, fragments and trailing line breaks. Return context does not grant any access. The original destination must pass the same current server admission and protected reads, including another-account, Portal and cross-Organization denial.

No new storage, query parameter, automatic command replay or session policy is introduced. The existing login intent/key, ambiguous-response recovery and validated account acknowledgment are unchanged. This slice covers explicit sign-in from an unavailable Board/Card; other session-expiry redirect entry points remain separate work.

## Measurable acceptance

| Child criterion | Validation |
| --- | --- |
| AC-01 | Generic unavailable entry → explicit sign-in → original authorized Board/Card, including idempotent hash-derived entity IDs. |
| AC-02 | Uncertain/mismatched acknowledgments cannot navigate; same-details retry retains the original key; Card edit survives canonical read and reload; Close returns to Board without Login in Back history. |
| AC-03 | Invalid return state falls back safely; invitation callback is unchanged; outsider and Portal-only identities receive no internal data/navigation, and existing cross-Organization denial stays authoritative. |
| AC-04 | Native keyboard operation at 320/390/768/1280px; 768px at 200% text; no page overflow; applicable Axe checks; developer visual inspection recorded separately from broader manual accessibility and release acceptance. |

The machine ledger contains one focused Task and separate automated component/browser Test records under this slice. Do not infer full PRD-01/02 completion from these criteria.

## Verification and integration boundary

The local browser topology uses the actual compiled Demo API and the current Vite frontend. Accounts, Organization, private Board, List and Card are created through real APIs. The workflow validates process-local canonical persistence and actual role decisions. It does not certify PostgreSQL/RLS, immutable release images, managed providers or Production restart durability. Existing Production, browser and immutable-release CI gates remain mandatory.

Run the new component return suite, related auth/Board/admission tests, the full frontend regression, web/browser types, lint/build, architecture/capacity guards, browser registry coverage and the new six-case browser file. Retain the entire unchanged three-case Board workflow result and evaluate any failure against the original application; do not relax deadlines, assertions, retries or omit a case to claim acceptance. Current pass/fail evidence and integration state are recorded in `progress.json`, the generated cycle report and the UX delivery report.

The presentation change is reversible by reverting this feature commit. It relocates no working workflow and changes no persisted data, so it needs no migration or disruptive-UX rollout flag. Keep the PR in draft until applicable current checks, review and target integration are satisfied.
