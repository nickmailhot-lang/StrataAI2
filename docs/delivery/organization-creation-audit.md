# Organization creation and first-use audit

Source: `ee5ac6f339f22a260e112821656a5a8cb2902486`. Existing implementation is retained. The canonical OrganizationRecord already includes all seven required fields and Owner/Admin/Member roles. The smaller legacy Domain class is not the complete API record.

| Requirement | Existing implementation and tests | Current conclusion |
| --- | --- | --- |
| PRD-03-VS-01/WS-FR-001 | `apps/web/src/features/organizations/OrganizationCreationDialog.tsx`, `src/StrataAI.Application/Organizations/OrganizationService.cs`, `tests/browser/board.spec.ts`, `tests/browser/organization-creation-retries.spec.ts` | Creation and original-intent recovery already implemented. Scoped controlled UI evidence is present; current first-use native/CI evidence must still be matched. |
| PRD-03-VS-01/WS-FR-002 | `src/StrataAI.Application/Organizations/OrganizationModels.cs`, `src/StrataAI.Application/Organizations/OrganizationService.cs`, `db/migrations/003_identity.sql` | Canonical OrganizationRecord includes Name,Description,LogoUrl,OwnerUserId,Status,CreatedAt,UpdatedAt. Do not infer missing fields from the smaller legacy Domain type. Verify actual created canonical values; no logo feature restart. |
| PRD-03-VS-01/WS-FR-007 | `src/StrataAI.Application/Organizations/OrganizationService.cs`, `src/StrataAI.Api/Organizations/OrganizationEndpoints.cs`, `apps/web/src/features/organizations/OrganizationHome.tsx`, `tests/StrataAI.Api.Tests/OrganizationBoardDirectoryTests.cs`, `tests/browser/organization-directory.spec.ts` | Authorized bounded directory implementation and tests exist. Private/archived/removed-membership current execution evidence remains separate from source presence. |
| PRD-03-VS-01/WS-FR-008 | `src/StrataAI.Application/Organizations/OrganizationModels.cs`, `tests/StrataAI.Api.Tests/OrganizationCommandTests.cs`, `tests/browser/organization-members.spec.ts` | Owner/Admin/Member are existing canonical roles. Owner creation and anonymous/public denial are not substitutes for all current role/permission checks. |
| PRD-03-VS-01/EXIT | `tests/browser/board.spec.ts`, `tests/browser/organization-context-name.spec.ts`, `docs/ux/feature-acceptance.md` | Existing continuous first-use flow already creates Organization, Board, List and Card, verifies original-ID recovery, edits/reload/Back/Forward, wrong-Organization denial and public read-only isolation. Its three profiles omit320px and200%text; add only these missing profiles and preserve all originals. |

All original business criteria remain Unverified pending their complete current evidence. The actual current controlled OrganizationHome/action/focus subset has78passes on byte-identical source; source-reference matches are not passing native tests.

The existing board.spec.ts already provides the full first-use workflow. Its original three cases and30s/zero-retry policy will remain. The bounded next deliverable adds320px and768px/200%text profiles, verifies actual sign-in and created canonical fields, and captures rendered results. No new Organization implementation, schema, provider, role or API is proposed. The caption feature staysTesting in frozenPR117; its independentCI/main integration remains pending.

## Current execution update

The preserved original three and the two added320px/200%text first-use cases all passed once (zero skips/retries) against the exact unchanged frontend and retained API/Worker. The new cases performed real UI sign-in and creation and checked all canonical Organization fields, ownership and initial role, followed by the original Board/List/Card recovery/edit/reload/return/isolation workflow. All original72assertion chains, base30-second config and original rate-pacing fixture remain. Reported durations, including30531ms for the complete tablet case, are retained; this is not a claim that fixture/cleanup wall time is under30seconds.

Allsix newviewport captures were inspected separately. [320px](evidence/cycle57-first-use-320.png) and [200%text](evidence/cycle57-first-use-200.png) safe synthetic captures are review artifacts. No independent newAxe run is claimed. Existing unchanged-product Axe evidence and future current fullCI remain separate.

Published117/R10 dotnet-quality also passed:the complete provider log reports1086and1433successful tests with3and2skips respectively. It does not attribute individual Organization case results, so it is contextual whole-backend evidence, not a fabricated per-case pass. Source/backend/test equality through native-onlyR11 is verified.

Current Task03/Test areTesting with localnative5Passed. The auditTask01/TRACE and allfivebusinessparent criteria remain unverified/InProgress pending complete evidence review and integration. No parent PRD orMVP completion is asserted.
