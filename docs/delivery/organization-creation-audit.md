# Organization creation and first-use audit

Source: `ee5ac6f339f22a260e112821656a5a8cb2902486`. Existing implementation is retained. The canonical OrganizationRecord already includes all seven required fields and Owner/Admin/Member roles. The smaller legacy Domain class is not the complete API record.

| Requirement | Existing implementation and tests | Current conclusion |
| --- | --- | --- |
| PRD-03-VS-01/WS-FR-001 | `apps/web/src/features/organizations/OrganizationCreationDialog.tsx`, `src/StrataAI.Application/Organizations/OrganizationService.cs`, `tests/browser/board.spec.ts`, `tests/browser/organization-creation-retries.spec.ts` | Creation and original-intent recovery already implemented. Scoped controlled UI evidence is present; current first-use native/CI evidence must still be matched. |
| PRD-03-VS-01/WS-FR-002 | `src/StrataAI.Application/Organizations/OrganizationModels.cs`, `src/StrataAI.Application/Organizations/OrganizationService.cs`, `db/migrations/003_identity.sql` | Canonical OrganizationRecord includes Name,Description,LogoUrl,OwnerUserId,Status,CreatedAt,UpdatedAt. Do not infer missing fields from the smaller legacy Domain type. Verify actual created canonical values; no logo feature restart. |
| PRD-03-VS-01/WS-FR-007 | `src/StrataAI.Application/Organizations/OrganizationService.cs`, `src/StrataAI.Api/Organizations/OrganizationEndpoints.cs`, `apps/web/src/features/organizations/OrganizationHome.tsx`, `tests/StrataAI.Api.Tests/OrganizationDirectoryTests.cs`, `tests/browser/organization-directory.spec.ts` | Authorized bounded directory implementation and tests exist. Private/archived/removed-membership current execution evidence remains separate from source presence. |
| PRD-03-VS-01/WS-FR-008 | `src/StrataAI.Application/Organizations/OrganizationModels.cs`, `tests/StrataAI.Api.Tests/OrganizationCommandTests.cs`, `tests/browser/organization-members.spec.ts` | Owner/Admin/Member are existing canonical roles. Owner creation and anonymous/public denial are not substitutes for all current role/permission checks. |
| PRD-03-VS-01/EXIT | `tests/browser/board.spec.ts`, `tests/browser/organization-context-name.spec.ts`, `docs/ux/feature-acceptance.md` | Existing continuous first-use flow already creates Organization, Board, List and Card, verifies original-ID recovery, edits/reload/Back/Forward, wrong-Organization denial and public read-only isolation. Its three profiles omit320px and200%text; add only these missing profiles and preserve all originals. |

All original business criteria remain Unverified pending their complete current evidence. The actual current controlled OrganizationHome/action/focus subset has78passes on byte-identical source; source-reference matches are not passing native tests.

The existing board.spec.ts already provides the full first-use workflow. Its original three cases and30s/zero-retry policy will remain. The bounded next deliverable adds320px and768px/200%text profiles, verifies actual sign-in and created canonical fields, and captures rendered results. No new Organization implementation, schema, provider, role or API is proposed. The caption feature staysTesting in frozenPR117; its independentCI/main integration remains pending.

## Current execution update

The preserved original three and the two added320px/200%text first-use cases all passed once (zero skips/retries) against the exact unchanged frontend and retained API/Worker. The new cases performed real UI sign-in and creation and checked all canonical Organization fields, ownership and initial role, followed by the original Board/List/Card recovery/edit/reload/return/isolation workflow. All original72assertion chains, base30-second config and original rate-pacing fixture remain. Reported durations, including30531ms for the complete tablet case, are retained; this is not a claim that fixture/cleanup wall time is under30seconds.

Allsix newviewport captures were inspected separately. [320px](evidence/cycle57-first-use-320.png) and [200%text](evidence/cycle57-first-use-200.png) safe synthetic captures are review artifacts. No independent newAxe run is claimed. Existing unchanged-product Axe evidence and future current fullCI remain separate.

Published117/R10 dotnet-quality also passed:the complete provider log reports1086and1433successful tests with3and2skips respectively. It does not attribute individual Organization case results, so it is contextual whole-backend evidence, not a fabricated per-case pass. Source/backend/test equality through native-onlyR11 is verified.

Current Task03/Test areTesting with localnative5Passed. The auditTask01/TRACE and allfivebusinessparent criteria remain unverified/InProgress pending complete evidence review and integration. No parent PRD orMVP completion is asserted.


## Retained current backend evidence and mapping correction

The actual PR116 normalized TRX artifact (run 39376779095, artifact 12245655552, qualified head `6dc9a223d0d5ff9da40e6203c8bf3ffc6228d090`, tested tree integrated as `db5762265b3dfd19aa28b03ed24ceec2fe2ec265`) provides 18 individually matched passing API cases: creation replay (6), Organization directory (1), member directory and permission changes (5), and metadata/logo/owner safeguards (6). All four test files are byte-identical at the qualified head, this audit source and the working tree. The original full report has five separate skips; none belongs to these 18 selected cases. This is reused execution evidence, not a newly executed suite or inherited check status for PR117/118.

The WS-FR-007 mapping now cites `OrganizationDirectoryTests.cs`, whose actual `/organizations/directory` case checks bounded pagination, no private name disclosure, removed memberships, deleting Organizations, invalid cursors and anonymous refusal. The earlier Board-directory reference covers a different consumer and remains visible in Git history. No implementation was restarted.

The audit task and all five business criteria remain open. PR117 also requires the offline header repair and fresh qualification; PR118 requires the inherited fix and its own required checks and integration. The 320px and 200% first-use profiles remain preserved. No parent PRD or MVP completion is asserted.


## First-use run after inherited source repair

All five unchanged first-use tests passed against offline-fixed frontend `c940a6501e64c96bb90ee2ce42a0396059c97d56` and test source `47745b34a4c2970d9c6168d85ffd4941313d4aeb`, with zero failures/skips/retries. This supersedes no historical result: the original R7-client execution remains separate. The exact configured 30-second limit and original release fixtures remain; reported complete-case durations above 30 seconds are retained. Six current captures were inspected and owned cleanup/protected Worker checks passed. New published-head checks and actual integration remain pending.
