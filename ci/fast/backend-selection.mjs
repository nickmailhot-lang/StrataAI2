import { basename } from 'node:path';
import { selectApiMethods } from './api-selection.mjs';

const folder = 'tests/StrataAI.Api.Tests/';
export const baselineApi = ['ApiHostTests.cs', 'BoardPermissionMatrixTests.cs', 'ApiErrorContractTests.cs'];
// Reviewed HTTP consumer anchors, not a claim of complete feature regression.
// Compile/domain coverage is retained alongside these methods; all persistence,
// Worker/provider and cross-feature cases remain in nightly/candidate testing.
const anchors = [
  [/Identity|Authentication|Session|Password|Credential|UserProfile|\/Auth(?:Endpoints|Contracts|Options|Service)\.cs$/i, 'IdentityLoginReplayTests.cs'],
  [/Invitation|Onboarding/i, 'InvitationBodyAcceptanceTests.cs'],
  [/Organization|StrataLot|Membership/i, 'OrganizationSettingsActorTests.cs'],
  [/Date|Timezone/i, 'CardDateTests.cs'],
  [/Attachment|Preview|Scanner/i, 'AttachmentApiTests.cs'],
  [/Search|Navigation/i, 'NavigationInteractionHttpTests.cs'],
  [/Retention/i, 'RetentionHoldHttpTests.cs'],
  [/Label/i, 'LabelManagementTests.cs'],
  [/Activity|Comment|Mention|Watch|Notification/i, 'ActivityFeedTests.cs'],
  [/Board|Permission|Grant/i, 'BoardCreationActorTests.cs'],
  [/Card|List|Checklist|Work|Job|Worker|Lease|Clock|Event|Queue|Outbox/i, 'WorkCommandScopeApiTests.cs'],
];

export function selectBackendTests(paths, inventory, readSource) {
  const files = new Set(baselineApi.map(f => folder + f));
  const review = [];
  for (const path of paths) {
    if (path.startsWith(folder) && path.endsWith('Tests.cs')) { files.add(path); continue; }
    if (!/^src\//.test(path)) continue;
    if (/^src\/StrataAI\.Domain\//.test(path)) {
      review.push({ path, coverage: 'Complete fast Domain suite and HTTP authority baseline' }); continue;
    }
    const name = basename(path);
    if (/^(Program|CorrelationIdMiddleware|UnexpectedApiErrorMiddleware|ApiErrorContract)\.cs$/.test(name)) {
      review.push({ path, coverage: 'Composed HTTP authority/Problem baseline; complete host regression deferred' }); continue;
    }
    const match = anchors.find(([pattern]) => pattern.test(path));
    if (!match) throw new Error(`Backend consumer mapping requires review: ${path}`);
    files.add(folder + match[1]);
    review.push({ path, coverage: match[1] });
  }
  return { ...selectApiMethods([...files], inventory, readSource), review,
    evidenceKind: 'In-process real HTTP host/Domain feature smoke; complete production persistence/provider qualification deferred' };
}
